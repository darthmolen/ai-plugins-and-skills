#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Self-test battery for session.ps1.

.DESCRIPTION
    Pure functions are called after dot-sourcing the script. Verbs run as child processes, so
    parameter binding, dispatch and exit codes are exercised the way an agent meets them. Every case
    gets its own session directory through AGENT_SESSIONS_DIR, so the real ~/.agent-sessions is
    never touched.

    The session and lock verbs began in ticket-start's script, and the helper that runs this script
    as a child process keeps that origin in its name, Invoke-Ticket.

    The frame for every case that writes session or lock files comes from the ticket-start stub: a
    session removes its own files and locks and never another session's, except when the caller
    names that session.

.NOTES
    Exit 0 iff every case passes. A case reported SKIP has not passed.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:SessionScript = Join-Path $PSScriptRoot '..' 'scripts' 'session.ps1'
. $script:SessionScript

$script:Pass = 0
$script:Fail = 0
$script:Skip = 0
$script:Temp = Join-Path ([System.IO.Path]::GetTempPath()) ('ss-' + [guid]::NewGuid().ToString('N').Substring(0, 12))
New-Item -ItemType Directory -Path $script:Temp | Out-Null

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

# Runs session.ps1 as a child process, the way an agent does, and parses its JSON.
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
        $all = & pwsh -NoProfile -NonInteractive -File $script:SessionScript @Arguments 2>&1
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

function Get-SessionFile([string] $Session) { Join-Path $env:AGENT_SESSIONS_DIR "$Session.json" }
function Get-LockFile([string] $Resource) { Join-Path $env:AGENT_SESSIONS_DIR 'locks' "$Resource.lock" }

function New-Session([string] $Repo = 'repo-a', [string] $Ticket = '1', [string] $Worktree = $script:Temp) {
    $started = Start-TicketSession -Repo $Repo -Ticket $Ticket -Worktree $Worktree -Branch "bug/$Ticket" -Mode plan
    $started.session
}

function Set-SessionUpdated([string] $Session, [datetime] $When) {
    $path = Get-SessionFile $Session
    $record = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $record.updated = $When.ToUniversalTime().ToString('o')
    Set-Content -LiteralPath $path -Value ($record | ConvertTo-Json -Depth 5)
}

Write-Host 'session.ps1 self-test' -ForegroundColor Cyan
Write-Host ''

# --- cycle 5: a lock has one holder ------------------------------------------------------------

Invoke-Case 'lock-second-acquire-refused' {
    $a = New-Session -Ticket '1'
    $b = New-Session -Ticket '2'
    $first = Invoke-Ticket -Arguments @('lock-acquire', '-Resource', 'port-4200', '-Session', $a, '-Purpose', 'frontend')
    Assert-Equal 0 $first.Exit "first acquire (stdout: $($first.Stdout))"
    $second = Invoke-Ticket -Arguments @('lock-acquire', '-Resource', 'port-4200', '-Session', $b, '-Purpose', 'frontend')
    Assert-Equal 2 $second.Exit 'second acquire'
    Assert-Equal $a (Get-Field (Get-Field $second.Json 'holder') 'session') 'holder named'
}

Invoke-Case 'lock-race-one-winner' {
    $sessions = 1..8 | ForEach-Object { New-Session -Ticket "$_" }
    $scriptPath = $script:SessionScript
    $root = $env:AGENT_SESSIONS_DIR
    $codes = $sessions | ForEach-Object -ThrottleLimit 8 -Parallel {
        $env:AGENT_SESSIONS_DIR = $using:root
        & pwsh -NoProfile -NonInteractive -File $using:scriptPath lock-acquire -Resource 'sql-localhost-1433-orders_test_db' -Session $_ -Purpose race | Out-Null
        $LASTEXITCODE
    }
    Assert-Equal 1 @($codes | Where-Object { $_ -eq 0 }).Count "winners among exit codes [$($codes -join ',')]"
    Assert-Equal 7 @($codes | Where-Object { $_ -eq 2 }).Count "refusals among exit codes [$($codes -join ',')]"
}

# --- cycle 6: only the holder releases; a confirmed break removes any lock ----------------------

Invoke-Case 'lock-release-not-holder' {
    $a = New-Session -Ticket '1'
    $b = New-Session -Ticket '2'
    Assert-Equal 0 (Invoke-Ticket -Arguments @('lock-acquire', '-Resource', 'port-5001', '-Session', $a, '-Purpose', 'api')).Exit 'acquire'
    $wrong = Invoke-Ticket -Arguments @('lock-release', '-Resource', 'port-5001', '-Session', $b)
    Assert-Equal 2 $wrong.Exit 'release by a non-holder'
    $status = Invoke-Ticket -Arguments @('lock-status', '-Resource', 'port-5001')
    Assert-Equal 2 $status.Exit 'still held'
    Assert-Equal $a (Get-Field (Get-Field $status.Json 'holder') 'session') 'still held by'
    Assert-Equal 0 (Invoke-Ticket -Arguments @('lock-release', '-Resource', 'port-5001', '-Session', $a)).Exit 'release by the holder'
    Assert-Equal 0 (Invoke-Ticket -Arguments @('lock-status', '-Resource', 'port-5001')).Exit 'free afterwards'
}

Invoke-Case 'lock-acquire-needs-session' {
    $run = Invoke-Ticket -Arguments @('lock-acquire', '-Resource', 'port-4200', '-Session', 'no-such-session', '-Purpose', 'x')
    Assert-Equal 4 $run.Exit 'exit code'
    Assert-True (-not (Test-Path -LiteralPath (Get-LockFile 'port-4200'))) 'no lock file written'
}

Invoke-Case 'lock-name-shape-refused' {
    $a = New-Session -Ticket '1'
    $run = Invoke-Ticket -Arguments @('lock-acquire', '-Resource', 'The Test DB', '-Session', $a, '-Purpose', 'x')
    Assert-Equal 4 $run.Exit 'exit code'
    Assert-Equal 'bad-resource-name' (Get-Field $run.Json 'error') 'error'
}

Invoke-Case 'lock-break-reports-holder' {
    $a = New-Session -Ticket '1'
    Assert-Equal 0 (Invoke-Ticket -Arguments @('lock-acquire', '-Resource', 'container-sql-server', '-Session', $a, '-Purpose', 'tests')).Exit 'acquire'
    $broken = Invoke-Ticket -Arguments @('lock-break', '-Resource', 'container-sql-server')
    Assert-Equal 0 $broken.Exit 'break'
    Assert-Equal $a (Get-Field (Get-Field $broken.Json 'broken') 'session') 'holder reported'
    Assert-Equal 0 (Invoke-Ticket -Arguments @('lock-status', '-Resource', 'container-sql-server')).Exit 'free afterwards'
}

# --- cycle 7: ending a session removes its own files only ---------------------------------------

Invoke-Case 'session-end-own-only' {
    $a = New-Session -Ticket '1'
    $b = New-Session -Ticket '2'
    foreach ($resource in 'port-4200', 'port-5001') {
        Assert-Equal 0 (Invoke-Ticket -Arguments @('lock-acquire', '-Resource', $resource, '-Session', $a, '-Purpose', 'x')).Exit "acquire $resource"
    }
    Assert-Equal 0 (Invoke-Ticket -Arguments @('lock-acquire', '-Resource', 'port-6000', '-Session', $b, '-Purpose', 'x')).Exit 'acquire port-6000'
    $end = Invoke-Ticket -Arguments @('session-end', '-Session', $a)
    Assert-Equal 0 $end.Exit 'session-end'
    Assert-True (-not (Test-Path -LiteralPath (Get-SessionFile $a))) "ended session's file removed"
    Assert-True (-not (Test-Path -LiteralPath (Get-LockFile 'port-4200'))) 'its first lock released'
    Assert-True (-not (Test-Path -LiteralPath (Get-LockFile 'port-5001'))) 'its second lock released'
    Assert-True (Test-Path -LiteralPath (Get-SessionFile $b)) "the other session's file kept"
    Assert-True (Test-Path -LiteralPath (Get-LockFile 'port-6000')) "the other session's lock kept"
}

# --- cycle 8: one live session per repository and ticket ---------------------------------------

Invoke-Case 'session-same-ticket-refused' {
    $first = Invoke-Ticket -Arguments @('session-start', '-Repo', 'Contoso.Orders', '-Ticket', '22744', '-Worktree', $script:Temp, '-Branch', 'bug/22744_x', '-Mode', 'plan')
    Assert-Equal 0 $first.Exit "first start (stdout: $($first.Stdout))"
    $a = Get-Field $first.Json 'session'
    $second = Invoke-Ticket -Arguments @('session-start', '-Repo', 'Contoso.Orders', '-Ticket', '22744', '-Worktree', $script:Temp, '-Branch', 'bug/22744_x', '-Mode', 'implement')
    Assert-Equal 2 $second.Exit 'second start on the same ticket'
    Assert-Equal $a (Get-Field @(Get-Field $second.Json 'sessions')[0] 'session') 'existing session shown'
    $other = Invoke-Ticket -Arguments @('session-start', '-Repo', 'Contoso.Orders', '-Ticket', '22531', '-Worktree', $script:Temp, '-Branch', 'feature/22531_x', '-Mode', 'plan')
    Assert-Equal 0 $other.Exit 'start on another ticket'
}

Invoke-Case 'session-replace-ends-leftover' {
    $a = New-Session -Repo 'Contoso.Orders' -Ticket '22744'
    $c = New-Session -Repo 'Contoso.Orders' -Ticket '22531'
    Assert-Equal 0 (Invoke-Ticket -Arguments @('lock-acquire', '-Resource', 'port-4200', '-Session', $a, '-Purpose', 'x')).Exit 'acquire by the leftover'
    Assert-Equal 0 (Invoke-Ticket -Arguments @('lock-acquire', '-Resource', 'port-5001', '-Session', $c, '-Purpose', 'x')).Exit 'acquire by the other ticket'
    $run = Invoke-Ticket -Arguments @('session-start', '-Repo', 'Contoso.Orders', '-Ticket', '22744', '-Worktree', $script:Temp, '-Branch', 'bug/22744_x', '-Mode', 'implement', '-Replace', $a)
    Assert-Equal 0 $run.Exit "start replacing the leftover (stdout: $($run.Stdout))"
    Assert-True (-not (Test-Path -LiteralPath (Get-SessionFile $a))) "leftover's file removed"
    Assert-True (-not (Test-Path -LiteralPath (Get-LockFile 'port-4200'))) "leftover's lock removed"
    Assert-True (Test-Path -LiteralPath (Get-SessionFile $c)) "other ticket's file kept"
    Assert-True (Test-Path -LiteralPath (Get-LockFile 'port-5001')) "other ticket's lock kept"
}

Invoke-Case 'session-replace-other-ticket-refused' {
    $c = New-Session -Repo 'Contoso.Orders' -Ticket '22531'
    $run = Invoke-Ticket -Arguments @('session-start', '-Repo', 'Contoso.Orders', '-Ticket', '22744', '-Worktree', $script:Temp, '-Branch', 'bug/22744_x', '-Mode', 'plan', '-Replace', $c)
    Assert-Equal 4 $run.Exit 'exit code'
    Assert-True (Test-Path -LiteralPath (Get-SessionFile $c)) "the named session on another ticket is kept"
}

# --- cycle 9: what looks stale -----------------------------------------------------------------

Invoke-Case 'sessions-stale-needs-both' {
    $clean = New-GitRepo ''
    $active = New-GitRepo ''
    Set-Content -LiteralPath (Join-Path $active 'readme.txt') -Value 'edited now'
    $oldEdit = New-GitRepo ''
    $oldFile = Join-Path $oldEdit 'readme.txt'
    Set-Content -LiteralPath $oldFile -Value 'edited long ago'
    (Get-Item -LiteralPath $oldFile).LastWriteTimeUtc = [datetime]::UtcNow.AddMinutes(-60)

    $idle = New-Session -Ticket '1' -Worktree $clean
    $busy = New-Session -Ticket '2' -Worktree $active
    $fresh = New-Session -Ticket '3' -Worktree $clean
    $quiet = New-Session -Ticket '4' -Worktree $oldEdit
    $hourAgo = [datetime]::UtcNow.AddMinutes(-60)
    foreach ($session in $idle, $busy, $quiet) { Set-SessionUpdated $session $hourAgo }

    $run = Invoke-Ticket -Arguments @('sessions', '-StaleAfterMinutes', '30')
    Assert-Equal 0 $run.Exit "exit code (stdout: $($run.Stdout))"
    $bySession = @{}
    foreach ($entry in @(Get-Field $run.Json 'sessions')) { $bySession[(Get-Field $entry 'session')] = Get-Field $entry 'likelyStale' }
    Assert-Equal 'True' $bySession[$idle] 'old update, no worktree changes'
    Assert-Equal 'False' $bySession[$busy] 'old update, worktree changed now'
    Assert-Equal 'False' $bySession[$fresh] 'updated now'
    Assert-Equal 'True' $bySession[$quiet] 'old update, worktree changed an hour ago'
}

Invoke-Case 'sessions-orphan-lock-stale' {
    $live = New-Session -Ticket '1'
    Assert-Equal 0 (Invoke-Ticket -Arguments @('lock-acquire', '-Resource', 'port-4200', '-Session', $live, '-Purpose', 'x')).Exit 'acquire'
    $orphan = New-Session -Ticket '2'
    Assert-Equal 0 (Invoke-Ticket -Arguments @('lock-acquire', '-Resource', 'port-5001', '-Session', $orphan, '-Purpose', 'x')).Exit 'acquire'
    Remove-Item -LiteralPath (Get-SessionFile $orphan)
    $run = Invoke-Ticket -Arguments @('sessions')
    $byResource = @{}
    foreach ($entry in @(Get-Field $run.Json 'locks')) { $byResource[(Get-Field $entry 'resource')] = Get-Field $entry 'likelyStale' }
    Assert-Equal 'False' $byResource['port-4200'] 'lock of a live session'
    Assert-Equal 'True' $byResource['port-5001'] 'lock whose session file is gone'
}

Invoke-Case 'session-step-refreshes' {
    $clean = New-GitRepo ''
    $session = New-Session -Ticket '1' -Worktree $clean
    Set-SessionUpdated $session ([datetime]::UtcNow.AddMinutes(-60))
    Assert-Equal 0 (Invoke-Ticket -Arguments @('session-step', '-Session', $session, '-Step', 'implement')).Exit 'session-step'
    $entry = @(Get-Field (Invoke-Ticket -Arguments @('sessions')).Json 'sessions')[0]
    Assert-Equal 'implement' (Get-Field $entry 'step') 'step recorded'
    Assert-Equal 'False' (Get-Field $entry 'likelyStale') 'no longer stale'
}

# --- cycle 14: the mode set ---------------------------------------------------------------------

function Get-SessionFileCount { @(Get-ChildItem -LiteralPath $env:AGENT_SESSIONS_DIR -Filter '*.json' -File -ErrorAction SilentlyContinue).Count }

function Start-WithMode([string] $Ticket, [string] $Mode) {
    Invoke-Ticket -Arguments @('session-start', '-Repo', 'repo-a', '-Ticket', $Ticket, '-Worktree', $script:Temp, '-Branch', "bug/$Ticket", '-Mode', $Mode)
}

Invoke-Case 'session-mode-ticket-start-modes-accepted' {
    $n = 1
    foreach ($mode in 'plan', 'implement', 'ship') {
        $run = Start-WithMode "$n" $mode
        Assert-Equal 0 $run.Exit "mode $mode (stdout: $($run.Stdout))"
        Assert-Equal $mode (Get-Field $run.Json 'mode') "mode $mode recorded"
        $n++
    }
}

Invoke-Case 'session-mode-commit-accepted' {
    $run = Start-WithMode '1' 'commit'
    Assert-Equal 0 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal 'commit' (Get-Field $run.Json 'mode') 'mode recorded'
}

Invoke-Case 'session-mode-lead-accepted' {
    $run = Start-WithMode '1' 'lead'
    Assert-Equal 0 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal 'lead' (Get-Field $run.Json 'mode') 'mode recorded'
}

Invoke-Case 'session-mode-unknown-refused' {
    foreach ($mode in 'deploy', 'Plan', 'track') {
        $run = Start-WithMode '1' $mode
        Assert-Equal 4 $run.Exit "mode $mode"
        Assert-Equal 'bad-mode' (Get-Field $run.Json 'error') "error for $mode"
    }
    Assert-Equal 0 (Get-SessionFileCount) 'no session file written'
}

# --- cycle 15: the subject field ----------------------------------------------------------------

function Start-WithSubject([string] $Subject, [string] $Repo = 'repo-a') {
    Invoke-Ticket -Arguments @('session-start', '-Repo', $Repo, '-Subject', $Subject, '-Worktree', $script:Temp, '-Branch', 'feature/x', '-Mode', 'commit')
}

Invoke-Case 'session-subject-plan-recorded' {
    $run = Start-WithSubject 'plan:session-workflow-extraction_2026-11-19'
    Assert-Equal 0 $run.Exit "exit code (stdout: $($run.Stdout))"
    $session = Get-Field $run.Json 'session'
    $file = Get-Content -LiteralPath (Get-SessionFile $session) -Raw | ConvertFrom-Json
    Assert-Equal 'plan:session-workflow-extraction_2026-11-19' (Get-Field $file 'subject') 'subject in the file'
    Assert-True (-not ($file.PSObject.Properties.Name -contains 'context')) 'the file has no context field'
    Assert-True ($file.PSObject.Properties.Name -contains 'ticket') 'the file still carries a ticket field'
    Assert-Equal '' "$(Get-Field $file 'ticket')" 'ticket is null for a plan subject'
    $entry = @(Get-Field (Invoke-Ticket -Arguments @('sessions')).Json 'sessions')[0]
    Assert-Equal 'plan:session-workflow-extraction_2026-11-19' (Get-Field $entry 'subject') 'subject in sessions'
}

Invoke-Case 'session-subject-wave-id-shape' {
    $run = Start-WithSubject 'wave:wave-1/auth'
    Assert-Equal 0 $run.Exit "exit code (stdout: $($run.Stdout))"
    $session = "$(Get-Field $run.Json 'session')"
    Assert-True ($session -match '^repo-a_wave-wave-1-auth_\d{8}T\d{9}_[0-9a-f]{4}$') "session ID shape: $session"
    Assert-Equal 0 (Invoke-Ticket -Arguments @('session-step', '-Session', $session, '-Step', 'x')).Exit 'the ID works with session-step'
}

Invoke-Case 'session-ticket-flag-is-ticket-subject' {
    $run = Start-WithMode '22744' 'plan'
    Assert-Equal 0 $run.Exit "exit code (stdout: $($run.Stdout))"
    Assert-Equal 'ticket:22744' (Get-Field $run.Json 'subject') 'subject from -Ticket'
    Assert-Equal '22744' (Get-Field $run.Json 'ticket') 'ticket kept'
    Assert-True ("$(Get-Field $run.Json 'session')" -match '^repo-a_22744_') 'a ticket session ID keys on the number'
    $same = Start-WithSubject 'ticket:22744'
    Assert-Equal 2 $same.Exit '-Subject ticket:22744 is the same session as -Ticket 22744'
}

Invoke-Case 'session-subject-malformed-refused' {
    foreach ($subject in 'epic:1', 'plan:', 'plan', ':x', 'ticket:abc', 'Plan:x') {
        $run = Start-WithSubject $subject
        Assert-Equal 4 $run.Exit "subject '$subject' (stdout: $($run.Stdout))"
    }
    Assert-Equal 'bad-subject' (Get-Field (Start-WithSubject 'epic:1').Json 'error') 'error for an unknown kind'
    Assert-Equal 'bad-subject' (Get-Field (Start-WithSubject 'plan:').Json 'error') 'error for an empty value'
    Assert-Equal 0 (Get-SessionFileCount) 'no session file written'
}

Invoke-Case 'session-ticket-and-subject-refused' {
    $run = Invoke-Ticket -Arguments @('session-start', '-Repo', 'repo-a', '-Ticket', '1', '-Subject', 'ticket:1', '-Worktree', $script:Temp, '-Branch', 'b', '-Mode', 'plan')
    Assert-Equal 4 $run.Exit 'exit code'
    $none = Invoke-Ticket -Arguments @('session-start', '-Repo', 'repo-a', '-Worktree', $script:Temp, '-Branch', 'b', '-Mode', 'plan')
    Assert-Equal 4 $none.Exit 'neither -Ticket nor -Subject'
    Assert-Equal 0 (Get-SessionFileCount) 'no session file written'
}

Invoke-Case 'session-same-subject-refused' {
    Assert-Equal 0 (Start-WithSubject 'plan:a_2026-11-20').Exit 'first'
    $second = Start-WithSubject 'plan:a_2026-11-20'
    Assert-Equal 2 $second.Exit 'second on the same subject'
    Assert-Equal 'plan:a_2026-11-20' (Get-Field @(Get-Field $second.Json 'sessions')[0] 'subject') 'existing session shown with its subject'
    Assert-Equal 0 (Start-WithSubject 'plan:b_2026-11-20').Exit 'another plan'
    Assert-Equal 0 (Start-WithSubject 'plan:a_2026-11-20' -Repo 'repo-b').Exit 'same plan, another repository'
}

# An older file, written before the subject field existed, has a ticket and no subject.
function Write-TicketOnlySession([string] $Repo, [string] $Ticket) {
    $id = "$($Repo)_$($Ticket)_20261101T000000000_ab12"
    $now = [datetime]::UtcNow.ToString('o')
    $record = [ordered]@{ session = $id; repo = $Repo; ticket = $Ticket; worktree = $script:Temp; branch = "bug/$Ticket"; mode = 'implement'; step = 'implement'; started = $now; updated = $now }
    New-Item -ItemType Directory -Force -Path $env:AGENT_SESSIONS_DIR | Out-Null
    Set-Content -LiteralPath (Get-SessionFile $id) -Value ($record | ConvertTo-Json)
    $id
}

Invoke-Case 'session-ticket-only-file-reads-as-ticket-subject' {
    $old = Write-TicketOnlySession 'repo-a' '22744'
    $list = Invoke-Ticket -Arguments @('sessions')
    Assert-Equal 0 $list.Exit "sessions (stdout: $($list.Stdout))"
    $entry = @(Get-Field $list.Json 'sessions')[0]
    Assert-Equal $old (Get-Field $entry 'session') 'the older session is listed'
    Assert-Equal 'ticket:22744' (Get-Field $entry 'subject') 'a ticket-only file reads as ticket:<id>'
    $start = Start-WithSubject 'ticket:22744'
    Assert-Equal 2 $start.Exit 'the older session blocks the same ticket subject'
    Assert-Equal 'ticket:22744' (Get-Field @(Get-Field $start.Json 'sessions')[0] 'subject') 'shown with its read subject'
    Assert-Equal 0 (Invoke-Ticket -Arguments @('session-end', '-Session', $old)).Exit 'an older session still ends'
}

# An older file may carry context instead of subject.
function Write-ContextSession([string] $Repo, [string] $Context) {
    $id = "$($Repo)_plan-x_20261119T000000000_cd34"
    $now = [datetime]::UtcNow.ToString('o')
    $record = [ordered]@{ session = $id; context = $Context; repo = $Repo; ticket = $null; worktree = $script:Temp; branch = 'feature/x'; mode = 'commit'; step = 'implement'; started = $now; updated = $now }
    New-Item -ItemType Directory -Force -Path $env:AGENT_SESSIONS_DIR | Out-Null
    Set-Content -LiteralPath (Get-SessionFile $id) -Value ($record | ConvertTo-Json)
    $id
}

Invoke-Case 'session-context-file-reads-as-subject' {
    $old = Write-ContextSession 'repo-a' 'plan:x_2026-11-19'
    $list = Invoke-Ticket -Arguments @('sessions')
    Assert-Equal 0 $list.Exit "sessions (stdout: $($list.Stdout))"
    $entry = @(Get-Field $list.Json 'sessions')[0]
    Assert-Equal $old (Get-Field $entry 'session') 'the older session is listed'
    Assert-Equal 'plan:x_2026-11-19' (Get-Field $entry 'subject') 'its context reads as its subject'
    $start = Start-WithSubject 'plan:x_2026-11-19'
    Assert-Equal 2 $start.Exit 'the older session blocks the same subject'
    Assert-Equal 'plan:x_2026-11-19' (Get-Field @(Get-Field $start.Json 'sessions')[0] 'subject') 'shown with its read subject'
    Assert-Equal 0 (Start-WithSubject 'plan:x_2026-11-19' -Repo 'repo-b').Exit 'same subject, another repository'
    Assert-Equal 0 (Invoke-Ticket -Arguments @('session-end', '-Session', $old)).Exit 'an older session still ends'
    Assert-True (-not (Test-Path -LiteralPath (Get-SessionFile $old))) 'its file is removed'
}

# --- summary -----------------------------------------------------------------------------------

Remove-Item -LiteralPath $script:Temp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
Write-Host "$($script:Pass) passed, $($script:Fail) failed, $($script:Skip) skipped" -ForegroundColor Cyan
if ($script:Fail -gt 0 -or $script:Pass -eq 0) { exit 1 }
exit 0
