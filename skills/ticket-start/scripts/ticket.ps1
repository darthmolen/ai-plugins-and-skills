#!/usr/bin/env pwsh
<#
.SYNOPSIS
    The mechanical half of the ticket-start skill: the Azure DevOps REST calls. The session and
    lock files are session-workflow's scripts/session.ps1.

.DESCRIPTION
    One verb per call. The result is JSON on stdout, and the exit code is what the skill branches
    on.

      0  done
      1  unexpected error, or the Azure CLI is missing
      2  busy or conflicting; returned by session.ps1, never here
      3  the Azure login has expired or no token could be had; the JSON carries loginCommand, and
         tenant when the Azure CLI has one cached
      4  refused input: not an ADO remote, an unsupported work item type, a ticket session in a
         mode ticket-end refuses

    Azure DevOps verbs. Run them inside the repository: the org, project and repository come from
    origin, and the token from the existing Azure CLI login.

      repo
      workitem      -Id
      branch-name   -Id -Type -Title
      pr-find       -Branch

    The draft PR is opened through the ado-pr skill; this script has no verb for it.

    The ticket's session, for ticket-end. It reads session-workflow's sessions output and reaches
    no ADO.

      session-find  -Id -Repo

    The session and lock verbs (session-start, session-step, session-end, sessions, lock-acquire,
    lock-status, lock-release, lock-break) are session-workflow's: skills/session-workflow/scripts/
    session.ps1. The exit codes above are shared with it.

.NOTES
    PowerShell 7. Dot-sourcing this file defines its functions and runs nothing, which is how
    test/Test-TicketScript.ps1 reaches them.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)] [string] $Verb,
    [string] $Id,
    [string] $Type,
    [string] $Title,
    [string] $Branch,
    [string] $Repo,
    # Collects arguments no verb takes, so a call to a verb that does not exist
    # (pr-create -DescriptionFile ...) reaches the unknown-verb refusal instead of failing in
    # parameter binding.
    [Parameter(ValueFromRemainingArguments)] [string[]] $Unbound
)

$AdoResourceId = '499b84ac-1321-427f-aa17-267ca6975798'
$AdoApiVersion = 'api-version=7.0'
$AdoToken = $null

# The modes ticket-end ends a session in. Running /ticket-end is the consent to commit, push and
# open the draft PR, so an implement session is accepted; a plan session has nothing built.
$EndableModes = @('implement', 'ship')

# --- plumbing ----------------------------------------------------------------------------------

function Stop-Ticket {
    param([Parameter(Mandatory)][int] $Code, [Parameter(Mandatory)][System.Collections.IDictionary] $Payload)
    $exception = [System.Exception]::new([string] $Payload['message'])
    $exception.Data['ExitCode'] = $Code
    $exception.Data['Payload'] = $Payload
    throw $exception
}

function Get-JsonValue($Object, [string] $Name) {
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) { return $Object[$Name] }
    $property = $Object.PSObject.Properties[$Name]
    if ($property) { $property.Value } else { $null }
}

# --- Azure DevOps ------------------------------------------------------------------------------

function ConvertFrom-AdoRemote {
    param([string] $Url)
    $patterns = @(
        '^https://(?:[^@/]+@)?dev\.azure\.com/(?<org>[^/]+)/(?<project>[^/]+)/_git/(?<repo>[^/?#]+?)/?$'
        '^git@ssh\.dev\.azure\.com:v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>[^/]+?)/?$'
    )
    foreach ($pattern in $patterns) {
        if ("$Url".Trim() -match $pattern) {
            return [pscustomobject]@{ org = $Matches['org']; project = $Matches['project']; repo = $Matches['repo'] }
        }
    }
    $null
}

function Get-AdoRemote {
    $url = & git remote get-url origin 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $url) {
        Stop-Ticket 4 ([ordered]@{ error = 'no-origin'; message = 'This is not a git repository with an origin remote.' })
    }
    $parsed = ConvertFrom-AdoRemote "$url"
    if (-not $parsed) {
        Stop-Ticket 4 ([ordered]@{ error = 'not-ado-remote'; message = "origin is not an Azure DevOps remote: $url" })
    }
    $parsed
}

# An access token is a JWT, so anything else on stdout (an AADSTS message, a prompt to run
# az login, nothing at all) means there is no usable token.
function Test-TokenFailure {
    param([int] $ExitCode, [string] $Stdout)
    $ExitCode -ne 0 -or "$Stdout".Trim() -notmatch '^eyJ[\w-]*\.[\w-]*\.'
}

# ADO answers an expired login with 401, or with 203 and a sign-in page, or with HTML on a
# success status. Each of them stops the run.
function Get-ResponseKind {
    param([int] $StatusCode, [string] $ContentType, [string] $Body)
    if ($StatusCode -eq 401 -or $StatusCode -eq 203) { return 'login-expired' }
    if ($StatusCode -eq 404) { return 'not-found' }
    if ($StatusCode -ge 200 -and $StatusCode -lt 300) {
        if ($ContentType -match 'text/html' -or "$Body".TrimStart().StartsWith('<')) { return 'login-expired' }
        return 'ok'
    }
    'error'
}

function Get-CachedTenant {
    # az account show still reads the cached tenant after the token has expired.
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { return $null }
    $tenant = & az account show --query tenantId -o tsv 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    "$tenant".Trim()
}

function Stop-LoginExpired([string] $Reason) {
    $payload = [ordered]@{
        error   = 'login-expired'
        message = "$Reason. Run the login command in your own terminal, then invoke the skill again; it resumes at the step that stopped."
    }
    # With no cached tenant there is no tenant field and the command is bare; the skill asks the
    # person for their tenant.
    $tenant = Get-CachedTenant
    if ($tenant) {
        $payload.tenant = $tenant
        $payload.loginCommand = "az login --tenant $tenant"
    } else {
        $payload.loginCommand = 'az login'
    }
    Stop-Ticket 3 $payload
}

function Get-AdoToken {
    if ($script:AdoToken) { return $script:AdoToken }
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Stop-Ticket 1 ([ordered]@{ error = 'az-not-found'; message = 'The Azure CLI (az) is not on PATH. Install it and run az login.' })
    }
    $ErrorActionPreference = 'Continue'
    $all = & az account get-access-token --resource $AdoResourceId --query accessToken -o tsv 2>&1
    $code = $LASTEXITCODE
    $stdout = ($all | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join "`n"
    $stderr = ($all | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join ' '
    if (Test-TokenFailure -ExitCode $code -Stdout $stdout) {
        Stop-LoginExpired "The Azure CLI could not get an Azure DevOps token: $("$stderr $stdout".Trim())"
    }
    $script:AdoToken = $stdout.Trim()
    $script:AdoToken
}

function Invoke-AdoRequest {
    param([string] $Method = 'GET', [Parameter(Mandatory)][string] $Uri, $Body)
    $token = Get-AdoToken
    $request = @{
        Method             = $Method
        Uri                = $Uri
        Headers            = @{ Authorization = "Bearer $token" }
        SkipHttpErrorCheck = $true
    }
    if ($null -ne $Body) {
        $request.Body = $Body | ConvertTo-Json -Depth 10
        $request.ContentType = 'application/json'
    }
    $response = Invoke-WebRequest @request
    $status = [int] $response.StatusCode
    $contentType = if ($response.Headers.ContainsKey('Content-Type')) { @($response.Headers['Content-Type']) -join ';' } else { '' }
    $text = if ($response.Content -is [byte[]]) { [System.Text.Encoding]::UTF8.GetString($response.Content) } else { [string] $response.Content }
    switch (Get-ResponseKind -StatusCode $status -ContentType $contentType -Body $text) {
        'login-expired' { Stop-LoginExpired "Azure DevOps answered $status to $Method $Uri" }
        'error' {
            $excerpt = if ($text.Length -gt 300) { $text.Substring(0, 300) } else { $text }
            Stop-Ticket 1 ([ordered]@{ error = 'ado-error'; status = $status; message = "Azure DevOps answered $status to $Method $Uri`: $excerpt" })
        }
    }
    $json = $null
    if ($text) { try { $json = $text | ConvertFrom-Json } catch { $json = $null } }
    [pscustomobject]@{ Status = $status; Json = $json }
}

# The server's default branch is both the base and the PR target. A local origin/HEAD can be stale.
function Get-AdoRepository {
    $remote = Get-AdoRemote
    $uri = "https://dev.azure.com/$($remote.org)/$($remote.project)/_apis/git/repositories/$($remote.repo)?$AdoApiVersion"
    $response = Invoke-AdoRequest -Uri $uri
    if ($response.Status -eq 404) {
        Stop-Ticket 4 ([ordered]@{ error = 'not-found'; message = "Azure DevOps has no repository $($remote.project)/$($remote.repo) that this login can see." })
    }
    $default = "$(Get-JsonValue $response.Json 'defaultBranch')" -replace '^refs/heads/', ''
    if (-not $default) {
        Stop-Ticket 4 ([ordered]@{ error = 'no-default-branch'; message = "$($remote.repo) has no default branch on the server." })
    }
    [pscustomobject][ordered]@{ org = $remote.org; project = $remote.project; repo = $remote.repo; defaultBranch = $default }
}

function Get-WorkItem {
    param([Parameter(Mandatory)][string] $Id)
    if ($Id -notmatch '^\d+$') { Stop-Ticket 4 ([ordered]@{ error = 'bad-id'; message = "A work item ID is a number, not '$Id'." }) }
    $remote = Get-AdoRemote
    # Work item IDs are unique across the org, so the request needs no project.
    $uri = "https://dev.azure.com/$($remote.org)/_apis/wit/workitems/$($Id)?`$expand=relations&$AdoApiVersion"
    $response = Invoke-AdoRequest -Uri $uri
    if ($response.Status -eq 404) {
        Stop-Ticket 4 ([ordered]@{ error = 'not-found'; message = "Work item $Id does not exist, or this login cannot see it." })
    }
    $fields = Get-JsonValue $response.Json 'fields'
    $teamProject = "$(Get-JsonValue $fields 'System.TeamProject')"
    $relations = @(foreach ($relation in @(Get-JsonValue $response.Json 'relations')) {
            if ($null -eq $relation) { continue }
            [pscustomobject][ordered]@{
                rel  = "$(Get-JsonValue $relation 'rel')"
                name = "$(Get-JsonValue (Get-JsonValue $relation 'attributes') 'name')"
                url  = "$(Get-JsonValue $relation 'url')"
            }
        })
    [pscustomobject][ordered]@{
        id                 = [int] $Id
        type               = "$(Get-JsonValue $fields 'System.WorkItemType')"
        title              = "$(Get-JsonValue $fields 'System.Title')"
        state              = "$(Get-JsonValue $fields 'System.State')"
        teamProject        = $teamProject
        url                = "https://dev.azure.com/$($remote.org)/$([uri]::EscapeDataString($teamProject))/_workitems/edit/$Id"
        iterationPath      = "$(Get-JsonValue $fields 'System.IterationPath')"
        assignedTo         = "$(Get-JsonValue (Get-JsonValue $fields 'System.AssignedTo') 'displayName')"
        description        = "$(Get-JsonValue $fields 'System.Description')"
        acceptanceCriteria = "$(Get-JsonValue $fields 'Microsoft.VSTS.Common.AcceptanceCriteria')"
        reproSteps         = "$(Get-JsonValue $fields 'Microsoft.VSTS.TCM.ReproSteps')"
        relations          = $relations
    }
}

# Used only when local guidance and the repository's own branch names say nothing.
function Get-BranchFallback {
    param([Parameter(Mandatory)][string] $Id, [Parameter(Mandatory)][string] $Type, [string] $Title)
    $prefix = switch ($Type) {
        'User Story' { 'feature' }
        'Bug' { 'bug' }
        default { $null }
    }
    if (-not $prefix) { return $null }
    $slug = ("$Title".ToLowerInvariant() -replace '[^a-z0-9]+', '_').Trim('_')
    if ($slug.Length -gt 60) {
        $cut = $slug.Substring(0, 61).LastIndexOf('_')
        $slug = if ($cut -gt 0) { $slug.Substring(0, $cut) } else { $slug.Substring(0, 60) }
    }
    if ($slug) { "$prefix/$($Id)_$slug" } else { "$prefix/$Id" }
}

function Get-PullRequestUrl($RepoInfo, $PullRequestId) {
    "https://dev.azure.com/$($RepoInfo.org)/$($RepoInfo.project)/_git/$($RepoInfo.repo)/pullrequest/$PullRequestId"
}

function Find-ActivePullRequest {
    param([Parameter(Mandatory)] $RepoInfo, [Parameter(Mandatory)][string] $Branch)
    $base = "https://dev.azure.com/$($RepoInfo.org)/$($RepoInfo.project)/_apis/git/repositories/$($RepoInfo.repo)/pullrequests"
    $uri = "$($base)?searchCriteria.sourceRefName=refs/heads/$Branch&searchCriteria.status=active&$AdoApiVersion"
    $response = Invoke-AdoRequest -Uri $uri
    @(foreach ($pr in @(Get-JsonValue $response.Json 'value')) {
            if ($null -eq $pr) { continue }
            $prId = Get-JsonValue $pr 'pullRequestId'
            [pscustomobject][ordered]@{
                id      = $prId
                title   = "$(Get-JsonValue $pr 'title')"
                isDraft = [bool] (Get-JsonValue $pr 'isDraft')
                url     = Get-PullRequestUrl $RepoInfo $prId
            }
        })
}

# --- the ticket's session, for ticket-end ------------------------------------------------------

# session-workflow sits beside ticket-start, in the plugin and in ~/.claude/skills alike.
function Get-SessionScript {
    Join-Path $PSScriptRoot '..' '..' 'session-workflow' 'scripts' 'session.ps1'
}

# Reads session.ps1's sessions output, which already reads an older file's ticket or context
# field as its subject.
function Get-SessionViews {
    $path = Get-SessionScript
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Stop-Ticket 1 ([ordered]@{ error = 'no-session-workflow'; message = "session-workflow's session.ps1 is not at $path." })
    }
    $out = & pwsh -NoProfile -NonInteractive -File $path sessions
    if ($LASTEXITCODE -ne 0) {
        Stop-Ticket 1 ([ordered]@{ error = 'sessions-failed'; message = "session.ps1 sessions exited $LASTEXITCODE`: $($out -join ' ')" })
    }
    @(Get-JsonValue ($out -join "`n" | ConvertFrom-Json) 'sessions')
}

# The ticket's session in this repository, or none. A session in a mode ticket-end does not end is
# refused with exit 4, and the recorded mode is left as it is.
function Find-TicketSession {
    param([Parameter(Mandatory)][string] $Id, [Parameter(Mandatory)][string] $Repo, $Sessions)
    if ($Id -notmatch '^\d+$') { Stop-Ticket 4 ([ordered]@{ error = 'bad-id'; message = "A work item ID is a number, not '$Id'." }) }
    $found = @($Sessions | Where-Object { $null -ne $_ -and "$(Get-JsonValue $_ 'repo')" -eq $Repo -and "$(Get-JsonValue $_ 'subject')" -eq "ticket:$Id" })
    if ($found.Count -eq 0) { return [ordered]@{ session = $null } }
    $session = $found[0]
    $mode = "$(Get-JsonValue $session 'mode')"
    if ($EndableModes -cnotcontains $mode) {
        Stop-Ticket 4 ([ordered]@{
                error   = 'mode-refused'
                session = "$(Get-JsonValue $session 'session')"
                mode    = $mode
                message = "Ticket $Id's session is in $mode mode. ticket-end ends a session in $($EndableModes -join ' or '); a plan session has nothing built to ship."
            })
    }
    [ordered]@{ session = $session }
}

# --- verbs -------------------------------------------------------------------------------------

function Get-Required([string] $Name, [string] $Value) {
    if (-not $Value) { Stop-Ticket 4 ([ordered]@{ error = 'usage'; message = "$Verb needs -$Name." }) }
    $Value
}

if ($MyInvocation.InvocationName -ne '.') {
    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'
    try {
        if ($Unbound -and $Verb -in @('repo', 'workitem', 'branch-name', 'pr-find', 'session-find')) {
            Stop-Ticket 4 ([ordered]@{ error = 'usage'; message = "$Verb does not take: $($Unbound -join ' ')." })
        }
        $result = switch ($Verb) {
            'repo' { Get-AdoRepository }
            'workitem' { Get-WorkItem -Id (Get-Required 'Id' $Id) }
            'branch-name' {
                $name = Get-BranchFallback -Id (Get-Required 'Id' $Id) -Type (Get-Required 'Type' $Type) -Title $Title
                if (-not $name) {
                    Stop-Ticket 4 ([ordered]@{ error = 'unsupported-type'; message = "A $Type has no fallback branch name. Ask the caller for one." })
                }
                [ordered]@{ branch = $name }
            }
            'pr-find' {
                $info = Get-AdoRepository
                [ordered]@{ pullRequests = @(Find-ActivePullRequest -RepoInfo $info -Branch (Get-Required 'Branch' $Branch)) }
            }
            'session-find' {
                Find-TicketSession -Id (Get-Required 'Id' $Id) -Repo (Get-Required 'Repo' $Repo) -Sessions (Get-SessionViews)
            }
            default {
                Stop-Ticket 4 ([ordered]@{
                        error   = 'usage'
                        message = "Unknown verb '$Verb'. Verbs: repo, workitem, branch-name, pr-find, session-find. The draft PR is the ado-pr skill's. The session and lock verbs are session-workflow's scripts/session.ps1."
                    })
            }
        }
        $result | ConvertTo-Json -Depth 8
        exit 0
    } catch {
        $code = $_.Exception.Data['ExitCode']
        $payload = $_.Exception.Data['Payload']
        if ($null -eq $code) {
            $code = 1
            $payload = [ordered]@{ error = 'unexpected'; message = $_.Exception.Message }
        }
        $payload | ConvertTo-Json -Depth 8
        exit $code
    }
}
