<#
.SYNOPSIS
    Restores missing research repos from research/.manifest.yaml
.DESCRIPTION
    Called by the SessionStart hook to restore missing research repos.
    Reads research/.manifest.yaml, clones any missing repos (with optional
    sparse checkout), and prints a status summary to stdout.
    Always exits 0 to never block session start.
#>

$ErrorActionPreference = 'SilentlyContinue'

# Resolve repo root
$RepoRoot = git rev-parse --show-toplevel 2>$null
if (-not $RepoRoot) {
    Write-Output "Warning: Not inside a git repository. Cannot restore research library."
    exit 0
}

$Manifest = Join-Path $RepoRoot "research/.manifest.yaml"
$ResearchDir = Join-Path $RepoRoot "research"

if (-not (Test-Path $Manifest)) {
    Write-Output "No research manifest found at $Manifest"
    exit 0
}

# Read and parse the manifest
$content = Get-Content $Manifest -Raw

# Check for empty repos list
if ($content -match '^\s*repos:\s*\[\s*\]\s*$') {
    Write-Output "Research manifest is empty - no repos to restore."
    exit 0
}

# Simple YAML parser for our manifest format
$lines = Get-Content $Manifest
$repos = @()
$current = $null
$inSparse = $false

foreach ($line in $lines) {
    # Skip comments and empty lines
    if ($line -match '^\s*#' -or $line -match '^\s*$') { continue }

    # New repo entry (- name:)
    if ($line -match '^\s*-\s*name:\s*(.+)') {
        if ($current) { $repos += $current }
        $current = @{
            name   = $Matches[1].Trim()
            url    = ''
            ref    = ''
            sparse = @()
        }
        $inSparse = $false
        continue
    }

    # url field
    if ($line -match '^\s+url:\s*(.+)') {
        if ($current) { $current.url = $Matches[1].Trim() }
        $inSparse = $false
        continue
    }

    # ref field
    if ($line -match '^\s+ref:\s*(.+)') {
        if ($current) { $current.ref = $Matches[1].Trim() }
        $inSparse = $false
        continue
    }

    # sparse field (list header)
    if ($line -match '^\s+sparse:\s*$') {
        $inSparse = $true
        continue
    }

    # sparse list items
    if ($inSparse -and $line -match '^\s+-\s*(.+)') {
        if ($current) { $current.sparse += $Matches[1].Trim() }
        continue
    }

    # Any non-sparse-item line ends sparse parsing
    if ($inSparse) { $inSparse = $false }
}
# Don't forget the last entry
if ($current) { $repos += $current }

# Filter out entries with missing required fields
$repos = $repos | Where-Object { $_.name -and $_.url }

if ($repos.Count -eq 0) {
    Write-Output "Research manifest is empty - no repos to restore."
    exit 0
}

# Ensure research directory exists
if (-not (Test-Path $ResearchDir)) {
    New-Item -ItemType Directory -Path $ResearchDir -Force | Out-Null
}

$cloned = 0
$present = 0
$failed = 0
$statusLines = @()

# Suppress git credential prompts
$env:GIT_TERMINAL_PROMPT = '0'

foreach ($repo in $repos) {
    $repoDir = Join-Path $ResearchDir $repo.name

    if (Test-Path $repoDir) {
        $statusLines += "  [OK] $($repo.name) (already present)"
        $present++
        continue
    }

    $refDisplay = if ($repo.ref) { " @ $($repo.ref)" } else { '' }
    $cloneOk = $true

    if ($repo.sparse.Count -gt 0) {
        # Sparse checkout
        $result = git clone --filter=blob:none --sparse $repo.url $repoDir 2>&1
        if ($LASTEXITCODE -eq 0) {
            Push-Location $repoDir
            git sparse-checkout set @($repo.sparse) 2>$null
            Pop-Location
        } else {
            $cloneOk = $false
        }
    } else {
        # Full clone
        $result = git clone $repo.url $repoDir 2>&1
        if ($LASTEXITCODE -ne 0) {
            $cloneOk = $false
        }
    }

    if (-not $cloneOk) {
        $statusLines += "  [FAILED] $($repo.name) ($($repo.url) - clone failed, check URL/network/permissions)"
        $failed++
        continue
    }

    # Checkout specific ref if specified
    if ($repo.ref) {
        Push-Location $repoDir
        git checkout $repo.ref 2>$null
        Pop-Location
    }

    $sparseNote = if ($repo.sparse.Count -gt 0) { ' [sparse]' } else { '' }
    $statusLines += "  [CLONED] $($repo.name) ($($repo.url)$refDisplay)$sparseNote"
    $cloned++
}

# Print summary
Write-Output "Research Library Status ($cloned cloned, $present present, $failed failed):"
$statusLines | ForEach-Object { Write-Output $_ }

exit 0
