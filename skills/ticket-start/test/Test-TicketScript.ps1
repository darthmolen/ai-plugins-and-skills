#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Self-test battery for ticket.ps1.

.DESCRIPTION
    Pure functions are called after dot-sourcing the script. Verbs run as child processes, so
    parameter binding, dispatch and exit codes are exercised the way an agent meets them. Every case
    gets its own session directory through AGENT_SESSIONS_DIR, so the real ~/.agent-sessions is
    never touched.

    The ADO transport, Invoke-AdoRequest, is replaced only in the repository, work item and pull
    request cases, where the defect under test is in what the script decides rather than in how it
    talks to ADO. The real transport is reached by the expired-login case, which runs the Azure CLI
    against an empty configuration directory, and by the skill's end-to-end runs.

    The session and lock cases (cycles 5 to 9) are in session-workflow's
    test/Test-SessionScript.ps1, beside the verbs. The cycle numbers here keep their gap so the two
    batteries read as one sequence.

.NOTES
    Exit 0 iff every case passes. A case reported SKIP has not passed.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TicketScript = Join-Path $PSScriptRoot '..' 'scripts' 'ticket.ps1'
. $script:TicketScript

$script:Pass = 0
$script:Fail = 0
$script:Skip = 0
$script:Temp = Join-Path ([System.IO.Path]::GetTempPath()) ('ts-' + [guid]::NewGuid().ToString('N').Substring(0, 12))
New-Item -ItemType Directory -Path $script:Temp | Out-Null

$script:AdoHttps = 'https://your-org@dev.azure.com/your-org/Contoso.Orders/_git/Contoso.Orders'
$script:AdoStandards = 'https://your-org@dev.azure.com/your-org/Contoso.Billing/_git/Contoso.Billing'

# --- harness -----------------------------------------------------------------------------------

function New-TempDir {
    $dir = Join-Path $script:Temp ([guid]::NewGuid().ToString('N').Substring(0, 10))
    New-Item -ItemType Directory -Path $dir | Out-Null
    $dir
}

function New-GitRepo([string] $Origin) {
    $dir = New-TempDir
    git -C $dir init -q
    git -C $dir config user.email 'battery@example.com'
    git -C $dir config user.name 'battery'
    Set-Content -LiteralPath (Join-Path $dir 'readme.txt') -Value 'x'
    git -C $dir add readme.txt
    git -C $dir commit -q -m init
    if ($Origin) { git -C $dir remote add origin $Origin }
    $dir
}

function Invoke-Case {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][scriptblock] $Body)
    $env:AGENT_SESSIONS_DIR = New-TempDir
    try {
        $skipReason = & $Body
        if ($skipReason -is [string] -and $skipReason.StartsWith('SKIP:')) {
            $script:Skip++
            Write-Host "SKIP  $Name`: $($skipReason.Substring(5).Trim())" -ForegroundColor Yellow
            return
        }
        $script:Pass++
        Write-Host "PASS  $Name" -ForegroundColor Green
    } catch {
        $script:Fail++
        Write-Host "FAIL  $Name`: $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Assert-Equal($Expected, $Actual, [string] $What) {
    if ("$Expected" -cne "$Actual") { throw "$What`: expected '$Expected', got '$Actual'" }
}

function Assert-True($Condition, [string] $What) {
    if ($Condition -ne $true) { throw $What }
}

# Runs ticket.ps1 as a child process, the way an agent does, and parses its JSON.
function Invoke-Ticket {
    param(
        [string[]] $Arguments,
        [string] $WorkingDirectory = $script:Temp,
        [hashtable] $Environment = @{}
    )
    $saved = @{}
    foreach ($key in $Environment.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $Environment[$key])
    }
    Push-Location -LiteralPath $WorkingDirectory
    try {
        $all = & pwsh -NoProfile -NonInteractive -File $script:TicketScript @Arguments 2>&1
        $code = $LASTEXITCODE
    } finally {
        Pop-Location
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }
    $stdout = ($all | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join "`n"
    $stderr = ($all | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join "`n"
    $json = $null
    try { $json = $stdout | ConvertFrom-Json } catch { $json = $null }
    [pscustomobject]@{ Exit = $code; Json = $json; Stdout = $stdout; Stderr = $stderr }
}

function Get-Field($Object, [string] $Name) {
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) { return $Object[$Name] }
    $property = $Object.PSObject.Properties[$Name]
    if ($property) { $property.Value } else { $null }
}

# The exit code and payload a Stop-Ticket would turn into, for in-process calls.
function Invoke-Stopping([scriptblock] $Body) {
    try {
        $result = & $Body
        [pscustomobject]@{ Exit = 0; Payload = $result }
    } catch {
        $code = $_.Exception.Data['ExitCode']
        if ($null -eq $code) { throw }
        [pscustomobject]@{ Exit = [int] $code; Payload = $_.Exception.Data['Payload'] }
    }
}

# The fake transport. Responses are matched with -like against "METHOD uri".
$script:Requests = [System.Collections.Generic.List[object]]::new()
$script:Responses = [ordered] @{}

function Reset-Fake {
    $script:Requests.Clear()
    $script:Responses.Clear()
}

function Invoke-AdoRequest {
    param([string] $Method = 'GET', [string] $Uri, $Body)
    $script:Requests.Add([pscustomobject]@{ Method = $Method; Uri = $Uri; Body = $Body })
    foreach ($pattern in $script:Responses.Keys) {
        if ("$Method $Uri" -like $pattern) { return $script:Responses[$pattern] }
    }
    throw "the fake transport has no response for $Method $Uri"
}

function New-Response([int] $Status, $Json) { [pscustomobject]@{ Status = $Status; Json = $Json } }

$script:RepoInfo = [pscustomobject]@{
    org           = 'your-org'
    project       = 'Contoso.Orders'
    repo          = 'Contoso.Orders'
    defaultBranch = 'main'
}

Write-Host 'ticket.ps1 self-test' -ForegroundColor Cyan
Write-Host ''

# --- cycle 1: remotes and the server's default branch ------------------------------------------

Invoke-Case 'remote-https' {
    $parsed = ConvertFrom-AdoRemote $script:AdoHttps
    Assert-Equal 'your-org' (Get-Field $parsed 'org') 'org'
    Assert-Equal 'Contoso.Orders' (Get-Field $parsed 'project') 'project'
    Assert-Equal 'Contoso.Orders' (Get-Field $parsed 'repo') 'repo'
}

Invoke-Case 'remote-ssh' {
    $parsed = ConvertFrom-AdoRemote 'git@ssh.dev.azure.com:v3/your-org/Contoso.Billing/Contoso.Billing'
    Assert-Equal 'your-org' (Get-Field $parsed 'org') 'org'
    Assert-Equal 'Contoso.Billing' (Get-Field $parsed 'project') 'project'
    Assert-Equal 'Contoso.Billing' (Get-Field $parsed 'repo') 'repo'
}

Invoke-Case 'remote-unsupported-refused' {
    $repo = New-GitRepo 'https://gitlab.com/someone/something.git'
    $run = Invoke-Ticket -Arguments @('repo') -WorkingDirectory $repo
    Assert-Equal 4 $run.Exit 'exit code'
    Assert-Equal 'unsupported-remote' (Get-Field $run.Json 'error') 'error'
}

Invoke-Case 'remote-github-https' {
    $parsed = ConvertFrom-Remote 'https://github.com/someone/Contoso.Orders.git'
    Assert-Equal 'github' (Get-Field $parsed 'host') 'host'
    Assert-Equal 'someone' (Get-Field $parsed 'owner') 'owner'
    Assert-Equal 'Contoso.Orders' (Get-Field $parsed 'repo') 'repo, without .git'
}

Invoke-Case 'remote-github-ssh' {
    $parsed = ConvertFrom-Remote 'git@github.com:someone/Contoso.Orders.git'
    Assert-Equal 'github' (Get-Field $parsed 'host') 'host'
    Assert-Equal 'Contoso.Orders' (Get-Field $parsed 'repo') 'repo'
}

Invoke-Case 'remote-ado-host' {
    Assert-Equal 'ado' (Get-Field (ConvertFrom-Remote $script:AdoHttps) 'host') 'host'
}

Invoke-Case 'repo-default-branch-from-server' {
    Reset-Fake
    $script:Responses['GET https://dev.azure.com/your-org/Contoso.Orders/_apis/git/repositories/Contoso.Orders?api-version=7.0'] =
        New-Response 200 ([pscustomobject]@{ name = 'Contoso.Orders'; defaultBranch = 'refs/heads/main' })
    $repo = New-GitRepo $script:AdoHttps
    Push-Location -LiteralPath $repo
    try { $info = Get-AdoRepository } finally { Pop-Location }
    Assert-Equal 'main' (Get-Field $info 'defaultBranch') 'defaultBranch'
    Assert-Equal 'Contoso.Orders' (Get-Field $info 'project') 'project'
    Assert-Equal 1 $script:Requests.Count 'requests sent'
}

# --- cycle 2: the fallback branch name ---------------------------------------------------------

Invoke-Case 'branch-user-story' {
    Assert-Equal 'feature/22531_add_shipping_hold' (Get-BranchFallback -Id 22531 -Type 'User Story' -Title 'Add Shipping Hold') 'name'
}

Invoke-Case 'branch-bug' {
    Assert-Equal 'bug/22744_resend_keeps_the_original_timestamp' (Get-BranchFallback -Id 22744 -Type 'Bug' -Title 'Resend keeps the original timestamp') 'name'
}

Invoke-Case 'branch-punctuation' {
    Assert-Equal 'bug/1_fix_os_restore_null_when_no_osri' (Get-BranchFallback -Id 1 -Type 'Bug' -Title 'Fix: OS-Restore (null) when no OSRI!') 'name'
}

Invoke-Case 'branch-cut-at-underscore' {
    $title = 'alpha beta gamma dalet epsilon zeta eta theta iota kappa lambda mu'
    Assert-Equal 'feature/9_alpha_beta_gamma_dalet_epsilon_zeta_eta_theta_iota_kappa' (Get-BranchFallback -Id 9 -Type 'User Story' -Title $title) 'name'
}

Invoke-Case 'branch-feature' {
    Assert-Equal 'feature/12_add_export' (Get-BranchFallback -Id 12 -Type 'Feature' -Title 'Add export') 'name'
}

Invoke-Case 'branch-name-takes-a-hash-id' {
    $run = Invoke-Ticket -Arguments @('branch-name', '-Id', '#12', '-Type', 'Bug', '-Title', 'Crash')
    Assert-Equal 0 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal 'bug/12_crash' (Get-Field $run.Json 'branch') 'branch'
}

Invoke-Case 'branch-task-refused' {
    $run = Invoke-Ticket -Arguments @('branch-name', '-Id', '12807', '-Type', 'Task', '-Title', 'Anything')
    Assert-Equal 4 $run.Exit 'exit code'
    Assert-Equal 'unsupported-type' (Get-Field $run.Json 'error') 'error'
}

# --- cycle 3: recognising an expired login -----------------------------------------------------

Invoke-Case 'token-failed-call' {
    Assert-Equal 'True' (Test-TokenFailure -ExitCode 1 -Stdout '') 'failed call'
}

Invoke-Case 'token-aadsts-text' {
    Assert-Equal 'True' (Test-TokenFailure -ExitCode 0 -Stdout 'AADSTS700082: The refresh token has expired due to inactivity.') 'AADSTS text'
}

Invoke-Case 'token-jwt-ok' {
    Assert-Equal 'False' (Test-TokenFailure -ExitCode 0 -Stdout 'eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.e30.c2ln') 'a JWT'
}

Invoke-Case 'response-401' {
    Assert-Equal 'login-expired' (Get-ResponseKind -StatusCode 401 -ContentType 'application/json' -Body '{}') 'kind'
}

Invoke-Case 'response-203-any-body' {
    Assert-Equal 'login-expired' (Get-ResponseKind -StatusCode 203 -ContentType 'application/json; charset=utf-8' -Body '{"value":[]}') 'kind'
}

Invoke-Case 'response-html-on-200' {
    Assert-Equal 'login-expired' (Get-ResponseKind -StatusCode 200 -ContentType 'text/html; charset=utf-8' -Body '<!DOCTYPE html><html></html>') 'kind'
}

Invoke-Case 'response-json-200' {
    Assert-Equal 'ok' (Get-ResponseKind -StatusCode 200 -ContentType 'application/json; charset=utf-8' -Body '{"id":1}') 'kind'
}

Invoke-Case 'response-404' {
    Assert-Equal 'not-found' (Get-ResponseKind -StatusCode 404 -ContentType 'application/json' -Body '{"message":"x"}') 'kind'
}

# --- cycle 4: an expired login, end to end -----------------------------------------------------

Invoke-Case 'expired-login-exit-3' {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { return 'SKIP: the Azure CLI is not on PATH' }
    $repo = New-GitRepo $script:AdoHttps
    $run = Invoke-Ticket -Arguments @('repo') -WorkingDirectory $repo -Environment @{ AZURE_CONFIG_DIR = (New-TempDir) }
    Assert-Equal 3 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal 'login-expired' (Get-Field $run.Json 'error') 'error'
    # An empty configuration directory has no cached tenant: no tenant field, and a bare command.
    Assert-True ($null -eq $run.Json.PSObject.Properties['tenant']) "no tenant field: $($run.Stdout)"
    Assert-Equal 'az login' (Get-Field $run.Json 'loginCommand') 'loginCommand'
}

Invoke-Case 'expired-login-cached-tenant' {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { return 'SKIP: the Azure CLI is not on PATH' }
    # A profile with a subscription and no token cache: az account show reads the tenant, and the
    # token call fails, as after an expired login.
    $tenant = '11111111-2222-3333-4444-555555555555'
    $config = New-TempDir
    $profileJson = [ordered]@{
        installationId = '00000000-0000-0000-0000-000000000001'
        subscriptions  = @([ordered]@{
                id               = '00000000-0000-0000-0000-0000000000aa'
                name             = 'battery'
                state            = 'Enabled'
                user             = [ordered]@{ name = 'battery@example.com'; type = 'user' }
                isDefault        = $true
                tenantId         = $tenant
                homeTenantId     = $tenant
                environmentName  = 'AzureCloud'
                managedByTenants = @()
            })
    } | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText((Join-Path $config 'azureProfile.json'), $profileJson)
    $repo = New-GitRepo $script:AdoHttps
    $run = Invoke-Ticket -Arguments @('repo') -WorkingDirectory $repo -Environment @{ AZURE_CONFIG_DIR = $config }
    Assert-Equal 3 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal $tenant (Get-Field $run.Json 'tenant') 'tenant'
    Assert-Equal "az login --tenant $tenant" (Get-Field $run.Json 'loginCommand') 'loginCommand'
}

# --- cycle 14: the draft PR is the pr-create skill's, so pr-create is no verb --------------------

Invoke-Case 'pr-create-is-not-a-verb' {
    $repo = New-GitRepo $script:AdoHttps
    $file = Join-Path (New-TempDir) 'description.md'
    Set-Content -LiteralPath $file -Value 'd' -NoNewline
    $run = Invoke-Ticket -Arguments @('pr-create', '-Branch', 'bug/22744_x', '-Title', 't', '-DescriptionFile', $file) -WorkingDirectory $repo -Environment @{ AZURE_CONFIG_DIR = (New-TempDir) }
    Assert-Equal 4 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal 'usage' (Get-Field $run.Json 'error') 'error'
    Assert-True ("$(Get-Field $run.Json 'message')" -like "Unknown verb 'pr-create'*") "message: $(Get-Field $run.Json 'message')"
}

# --- cycle 16: the ticket as given, and the tracker it lives in -------------------------------

Invoke-Case 'ref-forms' {
    $cases = @(
        , @('22744', '22744', '', '')
        , @('#12', '12', 'github', '')
        , @('AB#22744', '22744', 'ado', '')
        , @('https://dev.azure.com/your-org/Contoso.Orders/_workitems/edit/22744', '22744', 'ado', 'your-org')
        , @('https://github.com/someone/Contoso.Orders/issues/12', '12', 'github', '')
    )
    foreach ($case in $cases) {
        $ref = ConvertFrom-TicketRef $case[0]
        Assert-Equal $case[1] $ref.id "id of $($case[0])"
        Assert-Equal $case[2] "$($ref.tracker)" "tracker of $($case[0])"
        Assert-Equal $case[3] "$($ref.org)" "org of $($case[0])"
    }
}

Invoke-Case 'ref-malformed-refused' {
    $outcome = Invoke-Stopping { ConvertFrom-TicketRef 'PROJ-12' }
    Assert-Equal 4 $outcome.Exit 'exit code'
    Assert-Equal 'bad-id' (Get-Field $outcome.Payload 'error') 'error'
}

$script:GhRemote = [pscustomobject]@{ host = 'github'; url = 'https://github.com/someone/Contoso.Orders'; owner = 'someone'; repo = 'Contoso.Orders' }
$script:AdoRemote = [pscustomobject]@{ host = 'ado'; url = $script:AdoHttps; org = 'your-org'; project = 'Contoso.Orders'; repo = 'Contoso.Orders' }

Invoke-Case 'tracker-bare-number-follows-host' {
    Assert-Equal 'github' (Resolve-Tracker -Ref (ConvertFrom-TicketRef '12') -Remote $script:GhRemote) 'on GitHub'
    Assert-Equal 'ado' (Resolve-Tracker -Ref (ConvertFrom-TicketRef '22744') -Remote $script:AdoRemote) 'on Azure DevOps'
}

Invoke-Case 'tracker-explicit-wins-for-bare-number' {
    # A GitHub repository that keeps its work in Azure Boards, as its local guidance says.
    Assert-Equal 'ado' (Resolve-Tracker -Ref (ConvertFrom-TicketRef '22744') -Explicit 'ado' -Remote $script:GhRemote) 'tracker'
}

Invoke-Case 'tracker-ab-on-github' {
    Assert-Equal 'ado' (Resolve-Tracker -Ref (ConvertFrom-TicketRef 'AB#22744') -Remote $script:GhRemote) 'tracker'
}

Invoke-Case 'tracker-conflict-refused' {
    $outcome = Invoke-Stopping { Resolve-Tracker -Ref (ConvertFrom-TicketRef 'AB#22744') -Explicit 'github' -Remote $script:GhRemote }
    Assert-Equal 4 $outcome.Exit 'exit code'
    Assert-Equal 'tracker-conflict' (Get-Field $outcome.Payload 'error') 'error'
}

Invoke-Case 'tracker-github-issue-on-ado-refused' {
    $outcome = Invoke-Stopping { Resolve-Tracker -Ref (ConvertFrom-TicketRef '#12') -Remote $script:AdoRemote }
    Assert-Equal 4 $outcome.Exit 'exit code'
    Assert-Equal 'unsupported-tracker' (Get-Field $outcome.Payload 'error') 'error'
}

Invoke-Case 'tracker-issue-in-other-repository-refused' {
    $outcome = Invoke-Stopping { Resolve-Tracker -Ref (ConvertFrom-TicketRef 'https://github.com/someone/Other/issues/12') -Remote $script:GhRemote }
    Assert-Equal 4 $outcome.Exit 'exit code'
    Assert-Equal 'other-repository' (Get-Field $outcome.Payload 'error') 'error'
}

Invoke-Case 'tracker-bad-value-refused' {
    $outcome = Invoke-Stopping { Resolve-Tracker -Ref (ConvertFrom-TicketRef '12') -Explicit 'jira' -Remote $script:GhRemote }
    Assert-Equal 4 $outcome.Exit 'exit code'
    Assert-Equal 'usage' (Get-Field $outcome.Payload 'error') 'error'
}

Invoke-Case 'ado-org-precedence' {
    Assert-Equal 'from-url' (Resolve-AdoOrg -Ref (ConvertFrom-TicketRef 'https://dev.azure.com/from-url/P/_workitems/edit/1') -AdoOrg 'from-flag' -Remote $script:GhRemote) 'URL first'
    Assert-Equal 'from-flag' (Resolve-AdoOrg -Ref (ConvertFrom-TicketRef 'AB#1') -AdoOrg 'from-flag' -Remote $script:AdoRemote) '-AdoOrg next'
    Assert-Equal 'your-org' (Resolve-AdoOrg -Ref (ConvertFrom-TicketRef '1') -Remote $script:AdoRemote) 'origin last'
}

Invoke-Case 'ado-org-missing-on-github-refused' {
    $outcome = Invoke-Stopping { Resolve-AdoOrg -Ref (ConvertFrom-TicketRef 'AB#22744') -Remote $script:GhRemote }
    Assert-Equal 4 $outcome.Exit 'exit code'
    Assert-Equal 'no-ado-org' (Get-Field $outcome.Payload 'error') 'error'
}

Invoke-Case 'workitem-ab-from-github-repo' {
    # The work item is read from the org -AdoOrg names, with the repository on GitHub.
    Reset-Fake
    $script:Responses['GET https://dev.azure.com/boards-org/_apis/wit/workitems/22744?*'] =
        New-Response 200 ([pscustomobject]@{ id = 22744; fields = [pscustomobject]@{ 'System.WorkItemType' = 'Bug'; 'System.Title' = 't'; 'System.TeamProject' = 'P' } })
    $repo = New-GitRepo 'https://github.com/someone/Contoso.Orders.git'
    Push-Location -LiteralPath $repo
    try { $item = Get-WorkItem -Id 'AB#22744' -AdoOrg 'boards-org' } finally { Pop-Location }
    Assert-Equal 'ado' (Get-Field $item 'tracker') 'tracker'
    Assert-Equal 'https://dev.azure.com/boards-org/P/_workitems/edit/22744' (Get-Field $item 'url') 'url'
}

# --- cycle 17: GitHub --------------------------------------------------------------------------

# The fake gh transport. Responses are matched with -like against the api path.
$script:GhCalls = [System.Collections.Generic.List[string]]::new()
$script:GhResponses = [ordered] @{}

function Reset-GhFake {
    $script:GhCalls.Clear()
    $script:GhResponses.Clear()
}

function Invoke-GhApi {
    param([string] $Path)
    $script:GhCalls.Add($Path)
    foreach ($pattern in $script:GhResponses.Keys) {
        if ($Path -like $pattern) { return $script:GhResponses[$pattern] }
    }
    throw "the fake gh transport has no response for $Path"
}

Invoke-Case 'gh-kind-ok' { Assert-Equal 'ok' (Get-GhResponseKind -ExitCode 0 -Stderr '') 'kind' }

Invoke-Case 'gh-kind-no-login' {
    Assert-Equal 'login-expired' (Get-GhResponseKind -ExitCode 4 -Stderr 'To get started with GitHub CLI, please run:  gh auth login') 'kind'
}

Invoke-Case 'gh-kind-bad-token' { Assert-Equal 'login-expired' (Get-GhResponseKind -ExitCode 1 -Stderr 'gh: Bad credentials (HTTP 401)') 'kind' }

Invoke-Case 'gh-kind-404' { Assert-Equal 'not-found' (Get-GhResponseKind -ExitCode 1 -Stderr 'gh: Not Found (HTTP 404)') 'kind' }

Invoke-Case 'gh-kind-500' { Assert-Equal 'error' (Get-GhResponseKind -ExitCode 1 -Stderr 'gh: Server Error (HTTP 500)') 'kind' }

Invoke-Case 'gh-repo-default-branch' {
    Reset-GhFake
    $script:GhResponses['repos/someone/Contoso.Orders'] = New-Response 200 ([pscustomobject]@{ default_branch = 'trunk' })
    $repo = New-GitRepo 'git@github.com:someone/Contoso.Orders.git'
    Push-Location -LiteralPath $repo
    try { $info = Get-Repository } finally { Pop-Location }
    Assert-Equal 'github' (Get-Field $info 'host') 'host'
    Assert-Equal 'trunk' (Get-Field $info 'defaultBranch') 'defaultBranch'
}

function New-Issue([hashtable] $Extra = @{}) {
    $issue = [ordered]@{
        number    = 12
        title     = 'Export drops the last row'
        state     = 'open'
        body      = '## Steps'
        html_url  = 'https://github.com/someone/Contoso.Orders/issues/12'
        labels    = @([pscustomobject]@{ name = 'bug' }, [pscustomobject]@{ name = 'p1' })
        assignees = @([pscustomobject]@{ login = 'dev1' })
        milestone = [pscustomobject]@{ title = 'v2' }
        type      = $null
    }
    foreach ($key in $Extra.Keys) { $issue[$key] = $Extra[$key] }
    [pscustomobject] $issue
}

Invoke-Case 'gh-issue-fields' {
    Reset-GhFake
    $script:GhResponses['repos/someone/Contoso.Orders/issues/12'] = New-Response 200 (New-Issue)
    $repo = New-GitRepo 'https://github.com/someone/Contoso.Orders'
    Push-Location -LiteralPath $repo
    try { $item = Get-WorkItem -Id '#12' } finally { Pop-Location }
    Assert-Equal 'github' (Get-Field $item 'tracker') 'tracker'
    Assert-Equal 12 (Get-Field $item 'id') 'id'
    Assert-Equal 'Bug' (Get-Field $item 'type') 'type from the bug label'
    Assert-Equal 'Export drops the last row' (Get-Field $item 'title') 'title'
    Assert-Equal '## Steps' (Get-Field $item 'description') 'description is the body'
    Assert-Equal 'dev1' (Get-Field $item 'assignedTo') 'assignedTo'
    Assert-Equal 'v2' (Get-Field $item 'milestone') 'milestone'
    Assert-Equal 'bug p1' "$(Get-Field $item 'labels')" 'labels'
    Assert-Equal 'https://github.com/someone/Contoso.Orders/issues/12' (Get-Field $item 'url') 'url'
}

Invoke-Case 'gh-issue-type-wins-over-labels' {
    Assert-Equal 'Task' (Get-GitHubIssueType (New-Issue @{ type = [pscustomobject]@{ name = 'Task' } })) 'issue type'
}

Invoke-Case 'gh-issue-type-from-enhancement' {
    Assert-Equal 'Feature' (Get-GitHubIssueType (New-Issue @{ labels = @([pscustomobject]@{ name = 'enhancement' }) })) 'type'
}

Invoke-Case 'gh-issue-type-unknown-empty' {
    Assert-Equal '' (Get-GitHubIssueType (New-Issue @{ labels = @([pscustomobject]@{ name = 'debugging-tools' }) })) 'a label that only contains bug'
}

Invoke-Case 'gh-issue-is-pull-request-refused' {
    Reset-GhFake
    $script:GhResponses['repos/someone/Contoso.Orders/issues/13'] = New-Response 200 (New-Issue @{ number = 13; pull_request = [pscustomobject]@{ url = 'x' } })
    $outcome = Invoke-Stopping { Get-GitHubIssue -Id 13 -Owner 'someone' -Repo 'Contoso.Orders' }
    Assert-Equal 4 $outcome.Exit 'exit code'
    Assert-Equal 'is-pull-request' (Get-Field $outcome.Payload 'error') 'error'
}

Invoke-Case 'gh-issue-not-found' {
    Reset-GhFake
    $script:GhResponses['repos/someone/Contoso.Orders/issues/*'] = New-Response 404 $null
    $outcome = Invoke-Stopping { Get-GitHubIssue -Id 99999 -Owner 'someone' -Repo 'Contoso.Orders' }
    Assert-Equal 4 $outcome.Exit 'exit code'
    Assert-Equal 'not-found' (Get-Field $outcome.Payload 'error') 'error'
}

Invoke-Case 'gh-pr-find-open' {
    Reset-GhFake
    $script:GhResponses['repos/someone/Contoso.Orders/pulls?*'] =
        New-Response 200 @([pscustomobject]@{ number = 41; title = 'existing'; draft = $true; html_url = 'https://github.com/someone/Contoso.Orders/pull/41' })
    $info = [pscustomobject]@{ host = 'github'; owner = 'someone'; repo = 'Contoso.Orders'; defaultBranch = 'main' }
    $found = @(Find-GitHubPullRequest -RepoInfo $info -Branch 'bug/12_x')
    Assert-Equal 1 $found.Count 'open PRs'
    Assert-Equal 41 (Get-Field $found[0] 'id') 'id'
    Assert-Equal 'True' (Get-Field $found[0] 'isDraft') 'isDraft'
    Assert-Equal 'repos/someone/Contoso.Orders/pulls?head=someone%3Abug%2F12_x&state=open' $script:GhCalls[0] 'query'
}

Invoke-Case 'gh-pr-find-none' {
    Reset-GhFake
    $script:GhResponses['repos/someone/Contoso.Orders/pulls?*'] = New-Response 200 $null
    $info = [pscustomobject]@{ host = 'github'; owner = 'someone'; repo = 'Contoso.Orders'; defaultBranch = 'main' }
    Assert-Equal 0 @(Find-GitHubPullRequest -RepoInfo $info -Branch 'bug/12_x').Count 'open PRs'
}

Invoke-Case 'gh-expired-login-exit-3' {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { return 'SKIP: the GitHub CLI is not on PATH' }
    $repo = New-GitRepo 'https://github.com/someone/Contoso.Orders.git'
    $run = Invoke-Ticket -Arguments @('repo') -WorkingDirectory $repo -Environment @{ GH_CONFIG_DIR = (New-TempDir); GH_TOKEN = $null; GITHUB_TOKEN = $null }
    Assert-Equal 3 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal 'gh auth login' (Get-Field $run.Json 'loginCommand') 'loginCommand'
}

# --- cycle 15: session-find, the ticket's session and the mode rule ----------------------------

$script:SessionScript = Join-Path $PSScriptRoot '..' '..' 'session-workflow' 'scripts' 'session.ps1'

# Starts a real session through session.ps1, so these cases read the file session-workflow writes.
function New-Session([string] $Repo, [string] $Subject, [string] $Mode) {
    $out = & pwsh -NoProfile -NonInteractive -File $script:SessionScript session-start -Repo $Repo -Subject $Subject -Worktree (New-TempDir) -Branch 'bug/22744_x' -Mode $Mode
    if ($LASTEXITCODE -ne 0) { throw "session-start exited $LASTEXITCODE`: $out" }
    ($out -join "`n" | ConvertFrom-Json).session
}

Invoke-Case 'session-find-implement-accepted' {
    $id = New-Session 'Contoso.Orders' 'ticket:22744' 'implement'
    $run = Invoke-Ticket -Arguments @('session-find', '-Id', '22744', '-Repo', 'Contoso.Orders')
    Assert-Equal 0 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal $id (Get-Field (Get-Field $run.Json 'session') 'session') 'session'
    Assert-Equal 'implement' (Get-Field (Get-Field $run.Json 'session') 'mode') 'mode'
}

Invoke-Case 'session-find-ship-accepted' {
    $id = New-Session 'Contoso.Orders' 'ticket:22744' 'ship'
    $run = Invoke-Ticket -Arguments @('session-find', '-Id', '22744', '-Repo', 'Contoso.Orders')
    Assert-Equal 0 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal $id (Get-Field (Get-Field $run.Json 'session') 'session') 'session'
}

Invoke-Case 'session-find-plan-refused' {
    $id = New-Session 'Contoso.Orders' 'ticket:22744' 'plan'
    $run = Invoke-Ticket -Arguments @('session-find', '-Id', '22744', '-Repo', 'Contoso.Orders')
    Assert-Equal 4 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal 'mode-refused' (Get-Field $run.Json 'error') 'error'
    Assert-Equal 'plan' (Get-Field $run.Json 'mode') 'mode'
    Assert-Equal $id (Get-Field $run.Json 'session') 'session'
}

Invoke-Case 'session-find-hash-id' {
    $id = New-Session 'Contoso.Orders' 'ticket:12' 'implement'
    $run = Invoke-Ticket -Arguments @('session-find', '-Id', '#12', '-Repo', 'Contoso.Orders')
    Assert-Equal 0 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal $id (Get-Field (Get-Field $run.Json 'session') 'session') 'session'
}

Invoke-Case 'session-find-none' {
    # Another ticket's session, and this ticket's session in another repository, are not this one.
    New-Session 'Contoso.Orders' 'ticket:22531' 'implement' | Out-Null
    New-Session 'Contoso.Billing' 'ticket:22744' 'implement' | Out-Null
    $run = Invoke-Ticket -Arguments @('session-find', '-Id', '22744', '-Repo', 'Contoso.Orders')
    Assert-Equal 0 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-True ($null -eq (Get-Field $run.Json 'session')) "session: $($run.Stdout)"
}

Invoke-Case 'session-find-ticket-only-file' {
    # An older file has a ticket and no subject.
    $record = [ordered]@{
        session = 'Contoso.Orders_22744_20261101T000000000_abcd'; repo = 'Contoso.Orders'; ticket = '22744'
        worktree = (New-TempDir); branch = 'bug/22744_x'; mode = 'implement'; step = 'implement'
        started = '2026-11-01T00:00:00.0000000Z'; updated = '2026-11-01T00:00:00.0000000Z'
    }
    [System.IO.File]::WriteAllText((Join-Path $env:AGENT_SESSIONS_DIR "$($record.session).json"), ($record | ConvertTo-Json))
    $run = Invoke-Ticket -Arguments @('session-find', '-Id', '22744', '-Repo', 'Contoso.Orders')
    Assert-Equal 0 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal $record.session (Get-Field (Get-Field $run.Json 'session') 'session') 'session'
}

Invoke-Case 'session-find-context-file' {
    # An older file may carry context instead of subject.
    $record = [ordered]@{
        session = 'Contoso.Orders_22744_20261119T000000000_abcd'; context = 'ticket:22744'; repo = 'Contoso.Orders'
        ticket = '22744'; worktree = (New-TempDir); branch = 'bug/22744_x'; mode = 'plan'; step = 'plan'
        started = '2026-11-19T00:00:00.0000000Z'; updated = '2026-11-19T00:00:00.0000000Z'
    }
    [System.IO.File]::WriteAllText((Join-Path $env:AGENT_SESSIONS_DIR "$($record.session).json"), ($record | ConvertTo-Json))
    $run = Invoke-Ticket -Arguments @('session-find', '-Id', '22744', '-Repo', 'Contoso.Orders')
    Assert-Equal 4 $run.Exit "an older plan session is found and refused (stdout: $($run.Stdout))"
    Assert-Equal $record.session (Get-Field $run.Json 'session') 'session'
}

# --- cycles 10 to 12: active pull requests from the branch ---------------------------------------

Invoke-Case 'pr-find-active' {
    Reset-Fake
    $script:Responses['GET https://dev.azure.com/your-org/Contoso.Orders/_apis/git/repositories/Contoso.Orders/pullrequests?*'] =
        New-Response 200 ([pscustomobject]@{ count = 1; value = @([pscustomobject]@{ pullRequestId = 77; title = 'existing'; isDraft = $true }) })
    $found = @(Find-ActivePullRequest -RepoInfo $script:RepoInfo -Branch 'bug/22744_x')
    Assert-Equal 1 $found.Count 'active PRs'
    Assert-Equal 'https://dev.azure.com/your-org/Contoso.Orders/_git/Contoso.Orders/pullrequest/77' (Get-Field $found[0] 'url') 'url'
    Assert-Equal 'True' (Get-Field $found[0] 'isDraft') 'isDraft'
    Assert-True ($script:Requests[0].Uri -like '*searchCriteria.sourceRefName=refs/heads/bug/22744_x*searchCriteria.status=active*') "search: $($script:Requests[0].Uri)"
}

# --- cycle 13: the work item -------------------------------------------------------------------

Invoke-Case 'workitem-fields-and-url' {
    Reset-Fake
    $fields = [pscustomobject]@{
        'System.WorkItemType'              = 'Bug'
        'System.Title'                     = 'Resend keeps the original timestamp'
        'System.State'                     = 'Active'
        'System.TeamProject'               = 'Contoso.Orders'
        'System.IterationPath'             = 'Contoso.Orders\Sprint 1'
        'System.AssignedTo'                = [pscustomobject]@{ displayName = 'A Developer' }
        'System.Description'               = '<div>desc</div>'
        'Microsoft.VSTS.TCM.ReproSteps'    = '<div>steps</div>'
    }
    $relations = @([pscustomobject]@{ rel = 'System.LinkTypes.Hierarchy-Reverse'; url = 'https://dev.azure.com/your-org/_apis/wit/workItems/22000'; attributes = [pscustomobject]@{ name = 'Parent' } })
    $script:Responses['GET https://dev.azure.com/your-org/_apis/wit/workitems/22744?$expand=relations&api-version=7.0'] =
        New-Response 200 ([pscustomobject]@{ id = 22744; fields = $fields; relations = $relations })
    $repo = New-GitRepo $script:AdoStandards
    Push-Location -LiteralPath $repo
    try { $item = Get-WorkItem -Id 22744 } finally { Pop-Location }
    Assert-Equal 'Bug' (Get-Field $item 'type') 'type'
    Assert-Equal 'Resend keeps the original timestamp' (Get-Field $item 'title') 'title'
    Assert-Equal 'Contoso.Orders' (Get-Field $item 'teamProject') 'teamProject'
    Assert-Equal '<div>desc</div>' (Get-Field $item 'description') 'description'
    Assert-Equal '<div>steps</div>' (Get-Field $item 'reproSteps') 'reproSteps'
    Assert-Equal '' (Get-Field $item 'acceptanceCriteria') 'acceptanceCriteria when absent'
    Assert-Equal 'A Developer' (Get-Field $item 'assignedTo') 'assignedTo'
    Assert-Equal 'https://dev.azure.com/your-org/Contoso.Orders/_workitems/edit/22744' (Get-Field $item 'url') 'url uses the ticket''s project'
    Assert-Equal 'Parent' (Get-Field @(Get-Field $item 'relations')[0] 'name') 'relation name'
}

Invoke-Case 'workitem-not-found' {
    Reset-Fake
    $script:Responses['GET https://dev.azure.com/your-org/_apis/wit/workitems/*'] = New-Response 404 ([pscustomobject]@{ message = 'TF401232: Work item 99999999 does not exist' })
    $repo = New-GitRepo $script:AdoHttps
    Push-Location -LiteralPath $repo
    try { $outcome = Invoke-Stopping { Get-WorkItem -Id 99999999 } } finally { Pop-Location }
    Assert-Equal 4 $outcome.Exit 'exit code'
    Assert-Equal 'not-found' (Get-Field $outcome.Payload 'error') 'error'
}

# --- summary -----------------------------------------------------------------------------------

Remove-Item -LiteralPath $script:Temp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
Write-Host "$($script:Pass) passed, $($script:Fail) failed, $($script:Skip) skipped" -ForegroundColor Cyan
if ($script:Fail -gt 0 -or $script:Pass -eq 0) { exit 1 }
exit 0
