#!/usr/bin/env pwsh
<#
.SYNOPSIS
    The mechanical half of the session-workflow skill: the session and lock files that parallel
    sessions coordinate through.

.DESCRIPTION
    One verb per call. The result is JSON on stdout, and the exit code is what the skill branches
    on.

      0  done
      1  unexpected error
      2  busy or conflicting: a lock is held, or another session file exists for the subject
      4  refused input: a malformed name or subject, an unknown mode or session

    Exit 3 belongs to ticket-start's Azure DevOps verbs and is never returned here; the numbering
    is shared so a caller branches on one table.

    The files live in AGENT_SESSIONS_DIR, by default ~/.agent-sessions, with locks in its locks/
    folder.

      session-start -Repo (-Subject <kind>:<value> | -Ticket <id>) -Worktree -Branch -Mode
                    [-Replace <session>]
                    kind: ticket, plan, wave. mode: plan, implement, commit, ship, lead.
      session-step  -Session -Step
      session-end   -Session
      sessions      [-StaleAfterMinutes 30]
      lock-acquire  -Resource -Session -Purpose
      lock-status   -Resource
      lock-release  -Resource -Session
      lock-break    -Resource

.NOTES
    PowerShell 7. Dot-sourcing this file defines its functions and runs nothing, which is how
    test/Test-SessionScript.ps1 reaches them.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)] [string] $Verb,
    [string] $Repo,
    [string] $Ticket,
    [string] $Subject,
    [string] $Worktree,
    [string] $Branch,
    [string] $Mode,
    [string] $Replace,
    [string] $Session,
    [string] $Step,
    [int] $StaleAfterMinutes = 30,
    [string] $Resource,
    [string] $Purpose
)

$JsonKeepsDates = (Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')

# The mode set and the subject kinds. SKILL.md's consent table says what each
# mode may change; the script only refuses a mode that is not one of these.
$SessionModes = @('plan', 'implement', 'commit', 'ship', 'lead')
$SubjectKinds = @('ticket', 'plan', 'wave')

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

function Read-JsonFile([string] $Path) {
    $text = [System.IO.File]::ReadAllText($Path)
    if ($JsonKeepsDates) { $text | ConvertFrom-Json -DateKind String } else { $text | ConvertFrom-Json }
}

# Written to a temporary file and renamed into place, so a reader never sees half a file.
function Write-JsonFile([string] $Path, $Value) {
    $temp = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    [System.IO.File]::WriteAllText($temp, ($Value | ConvertTo-Json -Depth 6))
    [System.IO.File]::Move($temp, $Path, $true)
}

function ConvertTo-UtcTime($Value) {
    if ($Value -is [datetime]) { return $Value.ToUniversalTime() }
    if ($Value -is [DateTimeOffset]) { return $Value.UtcDateTime }
    [DateTimeOffset]::Parse("$Value", [Globalization.CultureInfo]::InvariantCulture).UtcDateTime
}

function Get-MinutesAgo($Value) {
    if ($null -eq $Value -or "$Value" -eq '') { return $null }
    [math]::Round(([datetime]::UtcNow - (ConvertTo-UtcTime $Value)).TotalMinutes, 1)
}

# --- sessions and locks ------------------------------------------------------------------------

function Get-SessionRoot {
    if ($env:AGENT_SESSIONS_DIR) { $env:AGENT_SESSIONS_DIR } else { Join-Path $HOME '.agent-sessions' }
}

function Get-LockRoot { Join-Path (Get-SessionRoot) 'locks' }

function Assert-SessionName([string] $Name) {
    if ($Name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
        Stop-Ticket 4 ([ordered]@{ error = 'bad-session'; message = "'$Name' is not a session ID." })
    }
}

# Two sessions must name the same resource the same way, so a name comes from the resource's
# address: port-4200, sql-localhost-1433-orders_test_db, container-sql-server, service-worker.
function Assert-ResourceName([string] $Name) {
    if ($Name -notmatch '^[a-z0-9][a-z0-9._-]*$') {
        Stop-Ticket 4 ([ordered]@{
                error   = 'bad-resource-name'
                message = "'$Name' is not a resource name. Use lowercase letters, digits, '.', '_' and '-', built from the resource's address: port-{n}, sql-{host}-{port}-{database}, container-{name} or service-{name}."
            })
    }
}

function Get-SessionRecords {
    $root = Get-SessionRoot
    if (-not (Test-Path -LiteralPath $root)) { return }
    foreach ($file in Get-ChildItem -LiteralPath $root -Filter '*.json' -File) {
        try { Read-JsonFile $file.FullName } catch { continue }
    }
}

function Read-LockFile([string] $Path) {
    # The file can be mid-write by the session taking it; the holder is then unknown, not absent.
    try { $data = Read-JsonFile $Path } catch { $data = $null }
    [pscustomobject][ordered]@{
        resource = [System.IO.Path]::GetFileNameWithoutExtension($Path)
        session  = "$(Get-JsonValue $data 'session')"
        purpose  = "$(Get-JsonValue $data 'purpose')"
        taken    = Get-JsonValue $data 'taken'
        file     = $Path
    }
}

function Get-LockRecords {
    $root = Get-LockRoot
    if (-not (Test-Path -LiteralPath $root)) { return }
    foreach ($file in Get-ChildItem -LiteralPath $root -Filter '*.lock' -File) { Read-LockFile $file.FullName }
}

function Get-LockView($Lock) {
    [ordered]@{
        resource       = $Lock.resource
        session        = $Lock.session
        purpose        = $Lock.purpose
        taken          = $Lock.taken
        takenMinutesAgo = Get-MinutesAgo $Lock.taken
    }
}

# What a session works on. An older file may call it context instead of subject. An older file
# still, written before either field existed, has only a ticket, and reads as ticket:<id>.
function Get-SessionSubject($Record) {
    $subject = "$(Get-JsonValue $Record 'subject')"
    if ($subject) { return $subject }
    $context = "$(Get-JsonValue $Record 'context')"
    if ($context) { return $context }
    "ticket:$(Get-JsonValue $Record 'ticket')"
}

# -Ticket <id> is shorthand for -Subject ticket:<id>. Exactly one of the two is given.
function Resolve-SessionSubject([string] $Ticket, [string] $Subject) {
    if ($Ticket -and $Subject) {
        Stop-Ticket 4 ([ordered]@{ error = 'usage'; message = 'Give -Ticket or -Subject, not both. -Ticket <id> means -Subject ticket:<id>.' })
    }
    if (-not $Ticket -and -not $Subject) {
        Stop-Ticket 4 ([ordered]@{ error = 'usage'; message = 'session-start needs -Subject <kind>:<value>, or -Ticket <id>.' })
    }
    if ($Ticket) { $Subject = "ticket:$Ticket" }
    $kind, $value = $Subject -split ':', 2
    if ($null -eq $value -or -not $value -or $SubjectKinds -cnotcontains $kind) {
        Stop-Ticket 4 ([ordered]@{
                error   = 'bad-subject'
                message = "'$Subject' is not a subject. Use <kind>:<value>, where <kind> is $($SubjectKinds -join ', ') and <value> is not empty."
            })
    }
    if ($kind -eq 'ticket' -and $value -notmatch '^\d+$') {
        Stop-Ticket 4 ([ordered]@{ error = 'bad-id'; message = "A ticket is the number of an issue or work item, without # or AB#, not '$value'." })
    }
    [pscustomobject]@{ subject = $Subject; kind = $kind; value = $value; ticket = if ($kind -eq 'ticket') { $value } else { $null } }
}

function Get-SessionView($Record) {
    [ordered]@{
        session          = $Record.session
        subject          = Get-SessionSubject $Record
        repo             = $Record.repo
        ticket           = $Record.ticket
        worktree         = $Record.worktree
        branch           = $Record.branch
        mode             = $Record.mode
        step             = $Record.step
        started          = $Record.started
        updated          = $Record.updated
        updatedMinutesAgo = Get-MinutesAgo $Record.updated
    }
}

# A ticket's ID part is its number. A plan's or a wave's is its subject, folded into the
# characters a session ID allows: wave:wave-1/auth becomes wave-wave-1-auth.
function New-SessionId([string] $Repo, $Resolved) {
    $slug = $Repo -replace '[^A-Za-z0-9._-]+', '-'
    $key = if ($Resolved.kind -eq 'ticket') { $Resolved.value } else { "$($Resolved.kind)-$($Resolved.value)" -replace '[^A-Za-z0-9._-]+', '-' }
    '{0}_{1}_{2}_{3}' -f $slug, $key, [datetime]::UtcNow.ToString('yyyyMMddTHHmmssfff'), [guid]::NewGuid().ToString('N').Substring(0, 4)
}

function Start-TicketSession {
    param(
        [Parameter(Mandatory)][string] $Repo,
        [string] $Ticket,
        [string] $Subject,
        [Parameter(Mandatory)][string] $Worktree,
        [Parameter(Mandatory)][string] $Branch,
        [Parameter(Mandatory)][string] $Mode,
        [string] $Replace
    )
    $resolved = Resolve-SessionSubject -Ticket $Ticket -Subject $Subject
    $Subject = $resolved.subject
    if ($SessionModes -cnotcontains $Mode) {
        Stop-Ticket 4 ([ordered]@{ error = 'bad-mode'; message = "'$Mode' is not a mode. Modes: $($SessionModes -join ', ')." })
    }
    $same = @(Get-SessionRecords | Where-Object { $_.repo -eq $Repo -and (Get-SessionSubject $_) -eq $Subject })
    if ($Replace) {
        # A session ID holds the repository name, which can contain dots, so only .json is stripped.
        $replaceId = (Split-Path -Leaf $Replace) -replace '\.json$', ''
        if (-not ($same | Where-Object { $_.session -eq $replaceId })) {
            Stop-Ticket 4 ([ordered]@{
                    error   = 'replace-not-found'
                    message = "No session for $Repo $Subject is named $replaceId. -Replace ends only a leftover session on the same subject."
                })
        }
        Remove-TicketSession -Session $replaceId | Out-Null
        $same = @($same | Where-Object { $_.session -ne $replaceId })
    }
    if ($same.Count -gt 0) {
        Stop-Ticket 2 ([ordered]@{
                error    = 'session-exists'
                message  = "A session file already exists for $Repo $Subject. If that session is still running, stop. If it is a leftover, show it to the caller, and only with their agreement start again with -Replace and its session ID."
                sessions = @($same | ForEach-Object { Get-SessionView $_ })
            })
    }
    $root = Get-SessionRoot
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $id = New-SessionId $Repo $resolved
    $now = [datetime]::UtcNow.ToString('o')
    $record = [ordered]@{
        session  = $id
        subject  = $Subject
        repo     = $Repo
        ticket   = $resolved.ticket
        worktree = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Worktree)
        branch   = $Branch
        mode     = $Mode
        step     = 'preflight'
        started  = $now
        updated  = $now
    }
    Write-JsonFile (Join-Path $root "$id.json") $record
    $record
}

function Set-TicketSessionStep {
    param([Parameter(Mandatory)][string] $Session, [Parameter(Mandatory)][string] $Step)
    Assert-SessionName $Session
    $path = Join-Path (Get-SessionRoot) "$Session.json"
    if (-not (Test-Path -LiteralPath $path)) {
        Stop-Ticket 4 ([ordered]@{ error = 'unknown-session'; message = "No session file for $Session." })
    }
    $record = Read-JsonFile $path
    $record.step = $Step
    $record.updated = [datetime]::UtcNow.ToString('o')
    Write-JsonFile $path $record
    $record
}

# Ends a session: its locks and its file, and nothing belonging to any other session.
function Remove-TicketSession {
    param([Parameter(Mandatory)][string] $Session)
    Assert-SessionName $Session
    $released = @(foreach ($lock in Get-LockRecords) { if ($lock.session -eq $Session) { Remove-Item -LiteralPath $lock.file -Force; $lock.resource } })
    $path = Join-Path (Get-SessionRoot) "$Session.json"
    $removed = Test-Path -LiteralPath $path
    if ($removed) { Remove-Item -LiteralPath $path -Force }
    [ordered]@{ session = $Session; removed = $removed; releasedLocks = $released }
}

# The newest change an agent made in a worktree: modified tracked files and untracked files that
# are not ignored. Build output is ignored, so it does not count as activity.
function Get-WorktreeActivity([string] $Worktree) {
    if (-not $Worktree -or -not (Test-Path -LiteralPath $Worktree)) { return $null }
    $listing = & git -C $Worktree ls-files -m -o --exclude-standard -z 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    $newest = $null
    foreach ($relative in ("$listing" -split "`0")) {
        if (-not $relative) { continue }
        $full = Join-Path $Worktree $relative
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            $time = (Get-Item -LiteralPath $full).LastWriteTimeUtc
            if ($null -eq $newest -or $time -gt $newest) { $newest = $time }
        }
    }
    $newest
}

function Get-TicketSessions {
    param([int] $StaleAfterMinutes = 30)
    $live = @{}
    $sessions = @(foreach ($record in Get-SessionRecords) {
            $view = Get-SessionView $record
            $activity = Get-WorktreeActivity "$($record.worktree)"
            $view.worktreeChangedMinutesAgo = if ($activity) { Get-MinutesAgo $activity } else { $null }
            # Likely stale only when the session has neither reported a step nor changed a file
            # for longer than the threshold. A long test run updates neither, so the threshold
            # must exceed the longest expected use.
            $quiet = $null -eq $view.worktreeChangedMinutesAgo -or $view.worktreeChangedMinutesAgo -gt $StaleAfterMinutes
            $view.likelyStale = $view.updatedMinutesAgo -gt $StaleAfterMinutes -and $quiet
            $live[$view.session] = $view.likelyStale
            $view
        })
    $locks = @(foreach ($lock in Get-LockRecords) {
            $view = Get-LockView $lock
            $view.sessionExists = $live.ContainsKey($lock.session)
            $view.likelyStale = -not $view.sessionExists -or $live[$lock.session]
            $view
        })
    [ordered]@{ staleAfterMinutes = $StaleAfterMinutes; sessions = $sessions; locks = $locks }
}

function Add-TicketLock {
    param([Parameter(Mandatory)][string] $Resource, [Parameter(Mandatory)][string] $Session, [Parameter(Mandatory)][string] $Purpose)
    Assert-ResourceName $Resource
    Assert-SessionName $Session
    if (-not (Test-Path -LiteralPath (Join-Path (Get-SessionRoot) "$Session.json"))) {
        Stop-Ticket 4 ([ordered]@{ error = 'unknown-session'; message = "No session file for $Session. Run session-start first." })
    }
    $root = Get-LockRoot
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $path = Join-Path $root "$Resource.lock"
    $record = [ordered]@{ resource = $Resource; session = $Session; purpose = $Purpose; taken = [datetime]::UtcNow.ToString('o') }
    try {
        # CreateNew fails when the file exists, so two sessions can never both take the lock.
        $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    } catch {
        if (-not (Test-Path -LiteralPath $path)) { throw }
        Stop-Ticket 2 ([ordered]@{ error = 'lock-held'; message = "$Resource is held by another session."; holder = Get-LockView (Read-LockFile $path) })
    }
    $writer = [System.IO.StreamWriter]::new($stream)
    try { $writer.Write(($record | ConvertTo-Json)) } finally { $writer.Dispose() }
    $record
}

function Get-TicketLockStatus {
    param([Parameter(Mandatory)][string] $Resource)
    Assert-ResourceName $Resource
    $path = Join-Path (Get-LockRoot) "$Resource.lock"
    if (Test-Path -LiteralPath $path) {
        Stop-Ticket 2 ([ordered]@{ error = 'lock-held'; message = "$Resource is held."; holder = Get-LockView (Read-LockFile $path) })
    }
    [ordered]@{ resource = $Resource; state = 'free' }
}

function Remove-TicketLock {
    param([Parameter(Mandatory)][string] $Resource, [Parameter(Mandatory)][string] $Session)
    Assert-ResourceName $Resource
    Assert-SessionName $Session
    $path = Join-Path (Get-LockRoot) "$Resource.lock"
    if (-not (Test-Path -LiteralPath $path)) { return [ordered]@{ resource = $Resource; released = $false; message = 'No lock was held.' } }
    $lock = Read-LockFile $path
    if ($lock.session -ne $Session) {
        Stop-Ticket 2 ([ordered]@{
                error   = 'not-holder'
                message = "$Resource is held by $($lock.session), not $Session. Only the holder releases a lock. A lock left by a session that died is removed with lock-break, after the caller agrees."
                holder  = Get-LockView $lock
            })
    }
    Remove-Item -LiteralPath $path -Force
    [ordered]@{ resource = $Resource; released = $true }
}

# Only after the caller has agreed that the holder is gone.
function Clear-TicketLock {
    param([Parameter(Mandatory)][string] $Resource)
    Assert-ResourceName $Resource
    $path = Join-Path (Get-LockRoot) "$Resource.lock"
    if (-not (Test-Path -LiteralPath $path)) { return [ordered]@{ resource = $Resource; broken = $null } }
    $lock = Read-LockFile $path
    Remove-Item -LiteralPath $path -Force
    [ordered]@{ resource = $Resource; broken = Get-LockView $lock }
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
        $result = switch ($Verb) {
            'session-start' {
                Start-TicketSession -Repo (Get-Required 'Repo' $Repo) -Ticket $Ticket -Subject $Subject `
                    -Worktree (Get-Required 'Worktree' $Worktree) -Branch (Get-Required 'Branch' $Branch) `
                    -Mode (Get-Required 'Mode' $Mode) -Replace $Replace
            }
            'session-step' { Set-TicketSessionStep -Session (Get-Required 'Session' $Session) -Step (Get-Required 'Step' $Step) }
            'session-end' { Remove-TicketSession -Session (Get-Required 'Session' $Session) }
            'sessions' { Get-TicketSessions -StaleAfterMinutes $StaleAfterMinutes }
            'lock-acquire' {
                Add-TicketLock -Resource (Get-Required 'Resource' $Resource) -Session (Get-Required 'Session' $Session) -Purpose (Get-Required 'Purpose' $Purpose)
            }
            'lock-status' { Get-TicketLockStatus -Resource (Get-Required 'Resource' $Resource) }
            'lock-release' { Remove-TicketLock -Resource (Get-Required 'Resource' $Resource) -Session (Get-Required 'Session' $Session) }
            'lock-break' { Clear-TicketLock -Resource (Get-Required 'Resource' $Resource) }
            default {
                Stop-Ticket 4 ([ordered]@{
                        error   = 'usage'
                        message = "Unknown verb '$Verb'. Verbs: session-start, session-step, session-end, sessions, lock-acquire, lock-status, lock-release, lock-break."
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
