# GitHub: the remote, the repository, issues and pull requests. Dot-sourced by ticket.ps1, which
# owns Stop-Ticket, Get-JsonValue, Get-Remote and Stop-LoginExpired. Every call goes through the
# gh CLI's existing login, so the script never holds a token.

function ConvertFrom-GitHubRemote {
    param([string] $Url)
    $patterns = @(
        '^https://(?:[^@/]+@)?github\.com/(?<owner>[^/]+)/(?<repo>[^/?#]+?)(?:\.git)?/?$'
        '^(?:ssh://)?git@github\.com[:/](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$'
    )
    foreach ($pattern in $patterns) {
        if ("$Url".Trim() -match $pattern) {
            return [pscustomobject]@{ owner = $Matches['owner']; repo = $Matches['repo'] }
        }
    }
    $null
}

# Every call names github.com, so a GH_HOST set for an Enterprise server cannot send a github.com
# remote's lookups there.
$GitHubHost = 'github.com'

# gh exits 4 when it has no login at all, and prints "HTTP 401" for a token GitHub refuses.
# A 404 is also what GitHub answers for a private repository this login cannot see.
function Get-GhResponseKind {
    param([int] $ExitCode, [string] $Stderr)
    if ($ExitCode -eq 0) { return 'ok' }
    if ($ExitCode -eq 4 -or $Stderr -match 'HTTP 401|gh auth login|not logged in') { return 'login-expired' }
    if ($Stderr -match 'HTTP 404') { return 'not-found' }
    'error'
}

function Invoke-GhApi {
    param([Parameter(Mandatory)][string] $Path)
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Stop-Ticket 1 ([ordered]@{ error = 'gh-not-found'; message = 'The GitHub CLI (gh) is not on PATH. Install it and run gh auth login.' })
    }
    $ErrorActionPreference = 'Continue'
    $all = & gh api --hostname $GitHubHost -H 'Accept: application/vnd.github+json' $Path 2>&1
    $code = $LASTEXITCODE
    $stdout = ($all | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join "`n"
    $stderr = ($all | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join ' '
    $status = 200
    switch (Get-GhResponseKind -ExitCode $code -Stderr $stderr) {
        'login-expired' { Stop-LoginExpired -Tool 'gh' -Reason "The GitHub CLI has no usable login: $($stderr.Trim())" }
        'not-found' { $status = 404 }
        'error' {
            Stop-Ticket 1 ([ordered]@{ error = 'github-error'; message = "gh api $Path exited $code`: $($stderr.Trim())" })
        }
    }
    $json = $null
    if ($status -eq 200 -and $stdout) { try { $json = $stdout | ConvertFrom-Json } catch { $json = $null } }
    [pscustomobject]@{ Status = $status; Json = $json }
}

function Get-GitHubRemote {
    $remote = Get-Remote
    if ($remote.host -ne 'github') {
        Stop-Ticket 4 ([ordered]@{ error = 'not-github-remote'; message = "origin is not a GitHub remote: $($remote.url)" })
    }
    $remote
}

function Get-GitHubRepository {
    $remote = Get-GitHubRemote
    $response = Invoke-GhApi -Path "repos/$($remote.owner)/$($remote.repo)"
    if ($response.Status -eq 404) {
        Stop-Ticket 4 ([ordered]@{ error = 'not-found'; message = "GitHub has no repository $($remote.owner)/$($remote.repo) that this login can see." })
    }
    $default = "$(Get-JsonValue $response.Json 'default_branch')"
    if (-not $default) {
        Stop-Ticket 4 ([ordered]@{ error = 'no-default-branch'; message = "$($remote.repo) has no default branch on the server." })
    }
    [pscustomobject][ordered]@{ host = 'github'; owner = $remote.owner; repo = $remote.repo; defaultBranch = $default }
}

# An issue's type is its issue type where the organization uses them, otherwise read from its
# labels. Anything else is left empty, and branch-name then asks.
function Get-GitHubIssueType {
    param($Issue)
    $typeName = "$(Get-JsonValue (Get-JsonValue $Issue 'type') 'name')"
    if ($typeName) { return $typeName }
    $labels = @(foreach ($label in @(Get-JsonValue $Issue 'labels')) { "$(Get-JsonValue $label 'name')".ToLowerInvariant() })
    if ($labels | Where-Object { $_ -match '(^|\W)bug(\W|$)' }) { return 'Bug' }
    if ($labels | Where-Object { $_ -match '(^|\W)(enhancement|feature)(\W|$)' }) { return 'Feature' }
    ''
}

function Get-GitHubIssue {
    param([Parameter(Mandatory)][string] $Id, [Parameter(Mandatory)][string] $Owner, [Parameter(Mandatory)][string] $Repo)
    $response = Invoke-GhApi -Path "repos/$Owner/$Repo/issues/$Id"
    if ($response.Status -eq 404) {
        Stop-Ticket 4 ([ordered]@{ error = 'not-found'; message = "Issue $Id does not exist in $Owner/$Repo, or this login cannot see it." })
    }
    $issue = $response.Json
    # The issues endpoint also answers for pull requests, which share the number space.
    if ($null -ne (Get-JsonValue $issue 'pull_request')) {
        Stop-Ticket 4 ([ordered]@{ error = 'is-pull-request'; message = "#$Id in $Owner/$Repo is a pull request, not an issue." })
    }
    [pscustomobject][ordered]@{
        id                 = [int] $Id
        tracker            = 'github'
        type               = Get-GitHubIssueType $issue
        title              = "$(Get-JsonValue $issue 'title')"
        state              = "$(Get-JsonValue $issue 'state')"
        url                = "$(Get-JsonValue $issue 'html_url')"
        assignedTo         = (@(foreach ($a in @(Get-JsonValue $issue 'assignees')) { if ($a) { "$(Get-JsonValue $a 'login')" } }) -join ', ')
        description        = "$(Get-JsonValue $issue 'body')"
        acceptanceCriteria = ''
        reproSteps         = ''
        relations          = @()
        labels             = @(foreach ($label in @(Get-JsonValue $issue 'labels')) { if ($label) { "$(Get-JsonValue $label 'name')" } })
        milestone          = "$(Get-JsonValue (Get-JsonValue $issue 'milestone') 'title')"
    }
}

function Find-GitHubPullRequest {
    param([Parameter(Mandatory)] $RepoInfo, [Parameter(Mandatory)][string] $Branch)
    $head = [uri]::EscapeDataString("$($RepoInfo.owner):$Branch")
    $response = Invoke-GhApi -Path "repos/$($RepoInfo.owner)/$($RepoInfo.repo)/pulls?head=$head&state=open"
    # A lookup that failed is not a branch with no PR: only a successful empty answer means none.
    if ($response.Status -eq 404) {
        Stop-Ticket 4 ([ordered]@{ error = 'not-found'; message = "GitHub has no repository $($RepoInfo.owner)/$($RepoInfo.repo) that this login can see, so its pull requests cannot be read." })
    }
    @(foreach ($pr in @($response.Json)) {
            if ($null -eq $pr) { continue }
            [pscustomobject][ordered]@{
                id      = Get-JsonValue $pr 'number'
                title   = "$(Get-JsonValue $pr 'title')"
                isDraft = [bool] (Get-JsonValue $pr 'draft')
                url     = "$(Get-JsonValue $pr 'html_url')"
            }
        })
}
