# Azure DevOps: the remote, the repository, work items and pull requests. Dot-sourced by ticket.ps1,
# which owns Stop-Ticket, Get-JsonValue, Get-Remote and Stop-LoginExpired. The token comes from the
# existing Azure CLI login.

$AdoResourceId = '499b84ac-1321-427f-aa17-267ca6975798'
$AdoApiVersion = 'api-version=7.0'
$AdoToken = $null

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
    $remote = Get-Remote
    if ($remote.host -ne 'ado') {
        Stop-Ticket 4 ([ordered]@{ error = 'not-ado-remote'; message = "origin is not an Azure DevOps remote: $($remote.url)" })
    }
    $remote
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
        Stop-LoginExpired -Tool 'az' -Reason "The Azure CLI could not get an Azure DevOps token: $("$stderr $stdout".Trim())"
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
        'login-expired' { Stop-LoginExpired -Tool 'az' -Reason "Azure DevOps answered $status to $Method $Uri" }
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
    [pscustomobject][ordered]@{ host = 'ado'; org = $remote.org; project = $remote.project; repo = $remote.repo; defaultBranch = $default }
}

function Get-AdoWorkItem {
    param([Parameter(Mandatory)][string] $Id, [Parameter(Mandatory)][string] $Org)
    # Work item IDs are unique across the org, so the request needs no project.
    $uri = "https://dev.azure.com/$Org/_apis/wit/workitems/$($Id)?`$expand=relations&$AdoApiVersion"
    $response = Invoke-AdoRequest -Uri $uri
    if ($response.Status -eq 404) {
        Stop-Ticket 4 ([ordered]@{ error = 'not-found'; message = "Work item $Id does not exist in $Org, or this login cannot see it." })
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
        tracker            = 'ado'
        type               = "$(Get-JsonValue $fields 'System.WorkItemType')"
        title              = "$(Get-JsonValue $fields 'System.Title')"
        state              = "$(Get-JsonValue $fields 'System.State')"
        url                = "https://dev.azure.com/$Org/$([uri]::EscapeDataString($teamProject))/_workitems/edit/$Id"
        assignedTo         = "$(Get-JsonValue (Get-JsonValue $fields 'System.AssignedTo') 'displayName')"
        description        = "$(Get-JsonValue $fields 'System.Description')"
        acceptanceCriteria = "$(Get-JsonValue $fields 'Microsoft.VSTS.Common.AcceptanceCriteria')"
        reproSteps         = "$(Get-JsonValue $fields 'Microsoft.VSTS.TCM.ReproSteps')"
        relations          = $relations
        teamProject        = $teamProject
        iterationPath      = "$(Get-JsonValue $fields 'System.IterationPath')"
    }
}

function Get-AdoPullRequestUrl($RepoInfo, $PullRequestId) {
    "https://dev.azure.com/$($RepoInfo.org)/$($RepoInfo.project)/_git/$($RepoInfo.repo)/pullrequest/$PullRequestId"
}

function Find-ActivePullRequest {
    param([Parameter(Mandatory)] $RepoInfo, [Parameter(Mandatory)][string] $Branch)
    $base = "https://dev.azure.com/$($RepoInfo.org)/$($RepoInfo.project)/_apis/git/repositories/$($RepoInfo.repo)/pullrequests"
    $uri = "$($base)?searchCriteria.sourceRefName=refs/heads/$Branch&searchCriteria.status=active&$AdoApiVersion"
    $response = Invoke-AdoRequest -Uri $uri
    # A lookup that failed is not a branch with no PR: only a successful empty answer means none.
    if ($response.Status -eq 404) {
        Stop-Ticket 4 ([ordered]@{ error = 'not-found'; message = "Azure DevOps has no repository $($RepoInfo.project)/$($RepoInfo.repo) that this login can see, so its pull requests cannot be read." })
    }
    @(foreach ($pr in @(Get-JsonValue $response.Json 'value')) {
            if ($null -eq $pr) { continue }
            $prId = Get-JsonValue $pr 'pullRequestId'
            [pscustomobject][ordered]@{
                id      = $prId
                title   = "$(Get-JsonValue $pr 'title')"
                isDraft = [bool] (Get-JsonValue $pr 'isDraft')
                url     = Get-AdoPullRequestUrl $RepoInfo $prId
            }
        })
}
