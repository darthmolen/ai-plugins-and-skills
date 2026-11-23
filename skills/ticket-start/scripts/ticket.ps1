#!/usr/bin/env pwsh
<#
.SYNOPSIS
    The mechanical half of the ticket-start skill: reading the repository, the ticket and its pull
    requests from Azure DevOps or GitHub. The session and lock files are session-workflow's
    scripts/session.ps1.

.DESCRIPTION
    One verb per call. The result is JSON on stdout, and the exit code is what the skill branches
    on.

      0  done
      1  unexpected error, or the Azure CLI or GitHub CLI is missing
      2  busy or conflicting; returned by session.ps1, never here
      3  the login has expired or no token could be had; the JSON carries loginCommand, and tenant
         when the Azure CLI has one cached
      4  refused input: a remote on neither host, a ticket the tracker or this repository cannot
         read, an unsupported work item type, a ticket session in a mode ticket-ship refuses

    The host comes from origin: Azure DevOps (dev.azure.com) or GitHub (github.com). The tracker,
    where the ticket lives, is GitHub Issues or Azure Boards, and need not be the host's own: a
    GitHub repository may keep its work in Azure Boards. A ticket is given as a number, #123,
    AB#123, or its URL. A bare number is read from -Tracker when given, else from the host's own
    tracker. Run the verbs inside the repository; the tokens come from the existing az and gh
    logins.

      repo
      workitem      -Id [-Tracker ado|github] [-AdoOrg org]
      branch-name   -Id -Type -Title
      pr-find       -Branch

    The draft PR is opened through the pr-create skill; this script has no verb for it.

    The ticket's session, for ticket-ship. It reads session-workflow's sessions output and reaches
    no server.

      session-find  -Id -Repo

    The session and lock verbs (session-start, session-step, session-end, sessions, lock-acquire,
    lock-status, lock-release, lock-break) are session-workflow's: skills/session-workflow/scripts/
    session.ps1. The exit codes above are shared with it.

.NOTES
    PowerShell 7. Dot-sourcing this file defines its functions and runs nothing, which is how
    test/Test-TicketScript.ps1 reaches them. The host-specific calls are in ticket-ado.ps1 and
    ticket-github.ps1 beside it.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)] [string] $Verb,
    [string] $Id,
    [string] $Type,
    [string] $Title,
    [string] $Branch,
    [string] $Repo,
    [string] $Tracker,
    [string] $AdoOrg,
    # Collects arguments no verb takes, so a call to a verb that does not exist
    # (pr-create -DescriptionFile ...) reaches the unknown-verb refusal instead of failing in
    # parameter binding.
    [Parameter(ValueFromRemainingArguments)] [string[]] $Unbound
)

. (Join-Path $PSScriptRoot 'ticket-ado.ps1')
. (Join-Path $PSScriptRoot 'ticket-github.ps1')

$Trackers = @('ado', 'github')
$Verbs = @('repo', 'workitem', 'branch-name', 'pr-find', 'session-find')

# The modes ticket-ship ends a session in. Running /ticket-ship is the consent to commit, push and
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

function Stop-LoginExpired {
    param([Parameter(Mandatory)][string] $Reason, [ValidateSet('az', 'gh')][string] $Tool = 'az')
    $payload = [ordered]@{
        error   = 'login-expired'
        message = "$Reason. Run the login command in your own terminal, then invoke the skill again; it resumes at the step that stopped."
    }
    if ($Tool -eq 'gh') {
        $payload.loginCommand = 'gh auth login'
        Stop-Ticket 3 $payload
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

# --- the host, from origin ---------------------------------------------------------------------

function ConvertFrom-Remote {
    param([string] $Url)
    $ado = ConvertFrom-AdoRemote $Url
    if ($ado) { return [pscustomobject]@{ host = 'ado'; url = $Url; org = $ado.org; project = $ado.project; repo = $ado.repo } }
    $github = ConvertFrom-GitHubRemote $Url
    if ($github) { return [pscustomobject]@{ host = 'github'; url = $Url; owner = $github.owner; repo = $github.repo } }
    $null
}

function Get-Remote {
    $url = & git remote get-url origin 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $url) {
        Stop-Ticket 4 ([ordered]@{ error = 'no-origin'; message = 'This is not a git repository with an origin remote.' })
    }
    $parsed = ConvertFrom-Remote "$url".Trim()
    if (-not $parsed) {
        Stop-Ticket 4 ([ordered]@{ error = 'unsupported-remote'; message = "origin is neither an Azure DevOps nor a GitHub remote: $url" })
    }
    $parsed
}

function Get-Repository {
    switch ((Get-Remote).host) {
        'ado' { Get-AdoRepository }
        'github' { Get-GitHubRepository }
    }
}

# --- the ticket, and which tracker it lives in -------------------------------------------------

$BadIdMessage = 'Give the ticket as a number, #123, AB#123, or its URL'

# What the caller typed, taken apart. tracker is null for a bare number, which the host decides.
function ConvertFrom-TicketRef {
    param([string] $Ref)
    $text = "$Ref".Trim()
    $parsed = switch -Regex ($text) {
        '^(?<id>\d+)$' { [pscustomobject]@{ id = $Matches['id']; tracker = $null; org = $null; owner = $null; repo = $null }; break }
        '^#(?<id>\d+)$' { [pscustomobject]@{ id = $Matches['id']; tracker = 'github'; org = $null; owner = $null; repo = $null }; break }
        '^AB#(?<id>\d+)$' { [pscustomobject]@{ id = $Matches['id']; tracker = 'ado'; org = $null; owner = $null; repo = $null }; break }
        '^https://(?:[^@/]+@)?dev\.azure\.com/(?<org>[^/]+)/[^/]+/_workitems/edit/(?<id>\d+)/?(?:[?#].*)?$' {
            [pscustomobject]@{ id = $Matches['id']; tracker = 'ado'; org = $Matches['org']; owner = $null; repo = $null }; break
        }
        '^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/issues/(?<id>\d+)/?(?:[?#].*)?$' {
            [pscustomobject]@{ id = $Matches['id']; tracker = 'github'; org = $null; owner = $Matches['owner']; repo = $Matches['repo'] }; break
        }
    }
    if (-not $parsed) { Stop-Ticket 4 ([ordered]@{ error = 'bad-id'; message = "$BadIdMessage, not '$Ref'." }) }
    $parsed
}

# Precedence: -Tracker (from local guidance), then the form the ticket was given in, then the
# host's own tracker.
function Resolve-Tracker {
    param([Parameter(Mandatory)] $Ref, [string] $Explicit, [Parameter(Mandatory)] $Remote)
    if ($Explicit -and $Trackers -cnotcontains $Explicit) {
        Stop-Ticket 4 ([ordered]@{ error = 'usage'; message = "-Tracker is $($Trackers -join ' or '), not '$Explicit'." })
    }
    if ($Explicit -and $Ref.tracker -and $Explicit -ne $Ref.tracker) {
        Stop-Ticket 4 ([ordered]@{ error = 'tracker-conflict'; message = "The ticket was given in $($Ref.tracker)'s form, but -Tracker says $Explicit." })
    }
    $resolved = if ($Explicit) { $Explicit } elseif ($Ref.tracker) { $Ref.tracker } else { $Remote.host }
    if ($resolved -eq 'github') {
        if ($Remote.host -ne 'github') {
            Stop-Ticket 4 ([ordered]@{ error = 'unsupported-tracker'; message = 'GitHub issues are read only in a GitHub repository; origin is on Azure DevOps.' })
        }
        if ($Ref.owner -and "$($Ref.owner)/$($Ref.repo)" -ne "$($Remote.owner)/$($Remote.repo)") {
            Stop-Ticket 4 ([ordered]@{ error = 'other-repository'; message = "Issue $($Ref.id) is in $($Ref.owner)/$($Ref.repo), not in this repository, $($Remote.owner)/$($Remote.repo)." })
        }
    }
    $resolved
}

# The Azure Boards organization: the ticket's URL, then -AdoOrg, then origin when it is on ADO.
function Resolve-AdoOrg {
    param([Parameter(Mandatory)] $Ref, [string] $AdoOrg, [Parameter(Mandatory)] $Remote)
    if ($Ref.org) { return $Ref.org }
    if ($AdoOrg) { return $AdoOrg }
    if ($Remote.host -eq 'ado') { return $Remote.org }
    Stop-Ticket 4 ([ordered]@{
            error   = 'no-ado-org'
            message = 'This repository is on GitHub and the ticket is in Azure Boards, so the organization cannot come from origin. Pass -AdoOrg, from local guidance or the caller, or give the work item URL.'
        })
}

function Get-WorkItem {
    param([Parameter(Mandatory)][string] $Id, [string] $Tracker, [string] $AdoOrg)
    $ref = ConvertFrom-TicketRef $Id
    $remote = Get-Remote
    switch (Resolve-Tracker -Ref $ref -Explicit $Tracker -Remote $remote) {
        'ado' { Get-AdoWorkItem -Id $ref.id -Org (Resolve-AdoOrg -Ref $ref -AdoOrg $AdoOrg -Remote $remote) }
        'github' { Get-GitHubIssue -Id $ref.id -Owner $remote.owner -Repo $remote.repo }
    }
}

# Used only when local guidance and the repository's own branch names say nothing.
function Get-BranchFallback {
    param([Parameter(Mandatory)][string] $Id, [Parameter(Mandatory)][string] $Type, [string] $Title)
    $prefix = switch ($Type) {
        'User Story' { 'feature' }
        'Feature' { 'feature' }
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

function Find-PullRequest {
    param([Parameter(Mandatory)][string] $Branch)
    $info = Get-Repository
    switch ($info.host) {
        'ado' { @(Find-ActivePullRequest -RepoInfo $info -Branch $Branch) }
        'github' { @(Find-GitHubPullRequest -RepoInfo $info -Branch $Branch) }
    }
}

# --- the ticket's session, for ticket-ship -----------------------------------------------------

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

# The ticket's session in this repository, or none. A session in a mode ticket-ship does not end is
# refused with exit 4, and the recorded mode is left as it is. The subject is ticket:<number>
# whichever tracker the ticket is in.
function Find-TicketSession {
    param([Parameter(Mandatory)][string] $Id, [Parameter(Mandatory)][string] $Repo, $Sessions)
    $number = (ConvertFrom-TicketRef $Id).id
    $found = @($Sessions | Where-Object { $null -ne $_ -and "$(Get-JsonValue $_ 'repo')" -eq $Repo -and "$(Get-JsonValue $_ 'subject')" -eq "ticket:$number" })
    if ($found.Count -eq 0) { return [ordered]@{ session = $null } }
    $session = $found[0]
    $mode = "$(Get-JsonValue $session 'mode')"
    if ($EndableModes -cnotcontains $mode) {
        Stop-Ticket 4 ([ordered]@{
                error   = 'mode-refused'
                session = "$(Get-JsonValue $session 'session')"
                mode    = $mode
                message = "Ticket $number's session is in $mode mode. ticket-ship ends a session in $($EndableModes -join ' or '); a plan session has nothing built to ship."
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
        if ($Unbound -and $Verb -in $Verbs) {
            Stop-Ticket 4 ([ordered]@{ error = 'usage'; message = "$Verb does not take: $($Unbound -join ' ')." })
        }
        $result = switch ($Verb) {
            'repo' { Get-Repository }
            'workitem' { Get-WorkItem -Id (Get-Required 'Id' $Id) -Tracker $Tracker -AdoOrg $AdoOrg }
            'branch-name' {
                $number = (ConvertFrom-TicketRef (Get-Required 'Id' $Id)).id
                $name = Get-BranchFallback -Id $number -Type (Get-Required 'Type' $Type) -Title $Title
                if (-not $name) {
                    Stop-Ticket 4 ([ordered]@{ error = 'unsupported-type'; message = "A $Type has no fallback branch name. Ask the caller for one." })
                }
                [ordered]@{ branch = $name }
            }
            'pr-find' { [ordered]@{ pullRequests = @(Find-PullRequest -Branch (Get-Required 'Branch' $Branch)) } }
            'session-find' {
                Find-TicketSession -Id (Get-Required 'Id' $Id) -Repo (Get-Required 'Repo' $Repo) -Sessions (Get-SessionViews)
            }
            default {
                Stop-Ticket 4 ([ordered]@{
                        error   = 'usage'
                        message = "Unknown verb '$Verb'. Verbs: $($Verbs -join ', '). The draft PR is the pr-create skill's. The session and lock verbs are session-workflow's scripts/session.ps1."
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
