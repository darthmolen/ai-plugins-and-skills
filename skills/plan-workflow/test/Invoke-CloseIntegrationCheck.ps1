#Requires -Version 7
<#
.SYNOPSIS
    Integration check for plan-doc.cs `close`. Covers what --selftest structurally cannot.

.DESCRIPTION
    `close` rewrites frontmatter AND runs `git mv`. The single most important line in it is
    the `git add` AFTER the mv: `git mv` stages the rename from the blob already in the
    index, so the frontmatter rewrite -- a working-tree change never added -- is left
    UNSTAGED, and the commit moves the file while committing its OLD content. Observed for
    real on ai-agents 533ed4e: a document landed in completed/ still saying
    status: in-progress.

    --selftest cannot reach this. It is a property of the git index, and the battery cannot
    invoke `close` in-process because dotnet run holds a lock on its own assembly. A mutant
    deleting the `git add` SURVIVED the battery, which under test-filter-development rejects
    the test rather than the mutant. This is that test.

    Black-box: it drives the real entry point against a real throwaway repository and reads
    the real git index, so it tests the contract a user actually experiences.

    Exits 0 if every assertion passes, 1 otherwise, so it can gate a commit.

.PARAMETER Script
    Path to plan-doc.cs. Defaults to ../scripts/plan-doc.cs relative to this file.

.PARAMETER KeepRepo
    Leave the throwaway repository on disk for inspection instead of deleting it.

.EXAMPLE
    .\Invoke-CloseIntegrationCheck.ps1

.EXAMPLE
    .\Invoke-CloseIntegrationCheck.ps1 -KeepRepo
#>
[CmdletBinding()]
param(
    [string] $Script,
    [switch] $KeepRepo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Script) {
    $Script = Join-Path (Split-Path $PSScriptRoot -Parent) 'scripts/plan-doc.cs'
}

if (-not (Test-Path -Path $Script -PathType Leaf)) {
    Write-Error "plan-doc.cs not found: $Script"
    exit 2
}

$Script = (Resolve-Path $Script).Path
$failures = 0
$checks = 0

function Assert-Equal {
    param([string] $Name, $Actual, $Expected)

    $script:checks++
    if ("$Actual" -eq "$Expected") {
        Write-Host "  ok    $Name"
    }
    else {
        Write-Host "  FAIL  $Name : expected '$Expected' got '$Actual'" -ForegroundColor Red
        $script:failures++
    }
}

# Run plan-doc and return only its exit code; output is noise here.
function Invoke-Close {
    param([string[]] $CloseArgs)

    & dotnet run $Script -- close @CloseArgs *>&1 | Out-Null
    return $LASTEXITCODE
}

function Count-Match {
    param([string] $Text, [string] $Pattern)

    if (-not $Text) { return 0 }
    return ([regex]::Matches($Text, $Pattern, 'Multiline')).Count
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("plan-doc-check-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
$origin = Get-Location

try {
    Set-Location $work
    git init -q .
    git config user.email 't@example.com'
    git config user.name 'Test'
    foreach ($d in 'planning/in-progress', 'planning/completed', 'planning/blocked', 'planning/not-implemented', 'planning/backlog') {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
    }

    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $doc = 'planning/in-progress/feature_thing_2026-09-14.md'
    [System.IO.File]::WriteAllText(
        (Join-Path $work $doc),
        "---`r`nkind: plan`r`nstatus: in-progress`r`ntrack: main`r`ndate: 2026-09-14`r`n---`r`n`r`n# Thing`r`n`r`nBody.`r`n",
        $utf8)
    [System.IO.File]::WriteAllText(
        (Join-Path $work 'block.md'),
        "## Status`r`n`r`n**Completed By:** test`r`n`r`n### Outcomes`r`n- done`r`n",
        $utf8)

    git add -A
    git commit -qm seed

    Write-Host '== close --to completed =='
    $rc = Invoke-Close @('--doc', $doc, '--to', 'completed', '--status-block', 'block.md', '--completed', '2026-09-14')
    Assert-Equal 'close exits 0' $rc 0

    $dest = 'planning/completed/feature_thing_2026-09-14.md'
    Assert-Equal 'the file moved' (Test-Path $dest) $true
    Assert-Equal 'the old path is gone' (Test-Path $doc) $false

    # THE ASSERTION THIS FILE EXISTS FOR. Read the STAGED blob, not the working tree: the
    # working tree is always right, and the index is what a commit would actually ship.
    $staged = (& git show ":$dest") -join "`n"
    Assert-Equal 'the staged blob says status: completed' (Count-Match $staged '^status: completed$') 1
    Assert-Equal 'the staged blob does NOT still say in-progress' (Count-Match $staged '^status: in-progress$') 0
    Assert-Equal 'the staged blob carries the completed date' (Count-Match $staged '^completed: 2026-09-14$') 1
    Assert-Equal 'the staged blob carries the Status section' (Count-Match $staged '^## Status$') 1

    # `git diff` is unstaged changes only. `git status --porcelain` would also list the
    # staged rename, which a successful close is SUPPOSED to leave behind.
    Assert-Equal 'nothing is left unstaged' (@(& git diff --name-only).Count) 0

    # Assert both halves of the move are staged -- NOT that git reports an "R" row. Whether
    # git calls it a rename is its similarity heuristic, and appending the Status section
    # pushes it below the threshold. That is git's business, not this script's, and a test
    # asserting it would be testing the wrong component.
    $cached = (& git diff --cached --name-status) -join "`n"
    Assert-Equal 'the destination is staged' (Count-Match $cached ([regex]::Escape($dest))) 1
    Assert-Equal 'the source is staged as deleted' (Count-Match $cached ('^D\s+' + [regex]::Escape($doc))) 1

    # A stub is a baby plan (plan-writing-syntax): it moves through the same directories under the
    # same verb. The prefix carries KIND, so promotion must NOT rename the file -- which
    # is the half of the change the in-process battery proves structurally and this one
    # proves against a real index, where a stray rename would show as two staged paths.
    Write-Host '== close a stub: promotion is completion, and the name does not change =='
    git commit -qm moved

    $stub = 'planning/backlog/stub_an-idea_2026-09-10.md'
    [System.IO.File]::WriteAllText(
        (Join-Path $work $stub),
        "---`r`nkind: stub`r`nstatus: backlog`r`ndate: 2026-09-10`r`n---`r`n`r`n# An idea`r`n`r`nBody.`r`n",
        $utf8)
    git add -A
    git commit -qm stub

    $rc = Invoke-Close @('--doc', $stub, '--to', 'completed', '--completed', '2026-09-16',
        '--promoted-to', 'thing_2026-09-14', '--decided-by', 'test', '--decided-on', '2026-09-16')
    Assert-Equal 'closing a stub exits 0' $rc 0

    $stubDest = 'planning/completed/stub_an-idea_2026-09-10.md'
    Assert-Equal 'the stub landed in completed/' (Test-Path $stubDest) $true
    Assert-Equal 'the stub kept its filename' (Split-Path $stubDest -Leaf) (Split-Path $stub -Leaf)

    $stubStaged = (& git show ":$stubDest") -join "`n"
    Assert-Equal 'the staged stub says status: completed' (Count-Match $stubStaged '^status: completed$') 1
    Assert-Equal 'the staged stub names the plan it became' (Count-Match $stubStaged '^promoted_to: thing_2026-09-14$') 1
    Assert-Equal 'the staged stub records who decided' (Count-Match $stubStaged '^decided_by: test$') 1
    Assert-Equal 'closing a stub leaves nothing unstaged' (@(& git diff --name-only).Count) 0

    Write-Host '== refusals =='
    git commit -qm promoted

    # A second close must refuse: the document already has a ## Status section.
    Assert-Equal 'refuses a document that already has a Status section' `
        (Invoke-Close @('--doc', $dest, '--to', 'blocked', '--blocked-on', 'x')) 1

    $d2 = 'planning/in-progress/feature_two_2026-09-14.md'
    [System.IO.File]::WriteAllText(
        (Join-Path $work $d2),
        "---`r`nkind: plan`r`nstatus: in-progress`r`ntrack: main`r`n---`r`n`r`n# Two`r`n",
        $utf8)
    git add -A
    git commit -qm two

    Assert-Equal 'refuses blocked with no --blocked-on' `
        (Invoke-Close @('--doc', $d2, '--to', 'blocked')) 2

    Assert-Equal 'refuses a path where a name belongs' `
        (Invoke-Close @('--doc', $d2, '--to', 'not-implemented', '--reason', 'superseded',
            '--reason-target', 'planning/completed/feature_thing_2026-09-14.md')) 2

    Assert-Equal 'refuses a status outside the vocabulary' `
        (Invoke-Close @('--doc', $d2, '--to', 'nonsense')) 2

    # close must not write a document its own validate verb would then reject.
    Assert-Equal 'refuses a decision date that is not ISO' `
        (Invoke-Close @('--doc', $d2, '--to', 'completed', '--decided-on', 'last tuesday')) 2

    Assert-Equal 'a refused close changed nothing' (@(& git status --porcelain).Count) 0

    # A document whose status and directory already disagree is not this tool's to fix.
    $d3 = 'planning/in-progress/feature_lying_2026-09-14.md'
    [System.IO.File]::WriteAllText(
        (Join-Path $work $d3),
        "---`r`nkind: plan`r`nstatus: completed`r`ntrack: main`r`ncompleted: 2026-09-14`r`n---`r`n`r`n# Lying`r`n",
        $utf8)
    git add -A
    git commit -qm lying

    Assert-Equal 'refuses a document already disagreeing with its directory' `
        (Invoke-Close @('--doc', $d3, '--to', 'blocked', '--blocked-on', 'x')) 1
}
finally {
    Set-Location $origin
    if ($KeepRepo) {
        Write-Host "`nrepository kept at $work" -ForegroundColor Cyan
    }
    else {
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }
}

Write-Host ''
if ($failures -eq 0) {
    Write-Host "integration: $checks/$checks checks passed" -ForegroundColor Green
    exit 0
}

Write-Host "integration: $failures of $checks check(s) failed" -ForegroundColor Red
exit 1
