<#
.SYNOPSIS
    Validates SKILL.md files against the Agent Skills specification (agentskills.io)
.DESCRIPTION
    Checks all SKILL.md files in the plugins directory for compliance with the
    Agent Skills open standard. Reports violations and warnings.
.LINK
    https://agentskills.io/specification
#>

param(
    [string]$PluginsDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "claude-code\plugins"),
    [switch]$Strict
)

$errors = 0
$warnings = 0

function Write-Issue {
    param([string]$Level, [string]$File, [string]$Message)
    $color = if ($Level -eq "ERROR") { "Red" } elseif ($Level -eq "WARN") { "Yellow" } else { "Cyan" }
    Write-Host "  [$Level] " -NoNewline -ForegroundColor $color
    Write-Host "$Message"
}

Write-Host "Agent Skills Spec Validation" -ForegroundColor Cyan
Write-Host "Spec: https://agentskills.io/specification" -ForegroundColor DarkGray
Write-Host ""

Get-ChildItem -Path $PluginsDir -Directory | ForEach-Object {
    $pluginDir = $_
    $skillsDir = Join-Path $pluginDir.FullName "skills"

    if (-not (Test-Path $skillsDir)) { return }

    Get-ChildItem -Path $skillsDir -Directory | Sort-Object Name | ForEach-Object {
        $skillDir = $_
        $skillMdPath = Join-Path $skillDir.FullName "SKILL.md"

        if (-not (Test-Path $skillMdPath)) {
            Write-Host "$($pluginDir.Name)/$($skillDir.Name)/" -ForegroundColor White
            Write-Issue "ERROR" $skillMdPath "Missing SKILL.md file"
            $script:errors++
            return
        }

        $content = Get-Content $skillMdPath -Raw
        Write-Host "$($pluginDir.Name)/$($skillDir.Name)/SKILL.md" -ForegroundColor White

        # Check frontmatter exists
        if ($content -notmatch '(?s)^---\s*\n(.*?)\n---') {
            Write-Issue "ERROR" $skillMdPath "Missing YAML frontmatter (--- delimiters)"
            $script:errors++
            return
        }

        $frontmatter = $Matches[1]

        # Check required: name
        if ($frontmatter -notmatch 'name:\s*(.+)') {
            Write-Issue "ERROR" $skillMdPath "Missing required field: name"
            $script:errors++
        } else {
            $name = $Matches[1].Trim().Trim('"').Trim("'")

            # name must match directory
            if ($name -ne $skillDir.Name) {
                Write-Issue "ERROR" $skillMdPath "name '$name' does not match directory '$($skillDir.Name)'"
                $script:errors++
            }

            # name format: 1-64 chars, lowercase+hyphens+numbers, no start/end hyphen, no consecutive hyphens
            if ($name.Length -gt 64) {
                Write-Issue "ERROR" $skillMdPath "name exceeds 64 characters ($($name.Length))"
                $script:errors++
            }
            if ($name -cmatch '[A-Z]') {
                Write-Issue "ERROR" $skillMdPath "name contains uppercase characters: '$name'"
                $script:errors++
            }
            if ($name -match '^-|-$') {
                Write-Issue "ERROR" $skillMdPath "name starts or ends with hyphen: '$name'"
                $script:errors++
            }
            if ($name -match '--') {
                Write-Issue "ERROR" $skillMdPath "name contains consecutive hyphens: '$name'"
                $script:errors++
            }
            if ($name -notmatch '^[a-z0-9][a-z0-9-]*[a-z0-9]$' -and $name.Length -gt 1) {
                Write-Issue "WARN" $skillMdPath "name may contain invalid characters: '$name'"
                $script:warnings++
            }
        }

        # Check required: description
        if ($frontmatter -notmatch 'description:\s*(.+)') {
            # Could be multiline with > or |
            if ($frontmatter -notmatch 'description:\s*[>|]') {
                Write-Issue "ERROR" $skillMdPath "Missing required field: description"
                $script:errors++
            }
        } else {
            $desc = $Matches[1].Trim().Trim('"').Trim("'")
            if ($desc.Length -gt 1024) {
                Write-Issue "WARN" $skillMdPath "description exceeds 1024 characters ($($desc.Length))"
                $script:warnings++
            }
        }

        # Check for non-standard fields (not in Agent Skills spec)
        $nonStandard = @('autoInvoke', 'priority', 'triggers', 'tools')
        foreach ($field in $nonStandard) {
            if ($frontmatter -match "^${field}:" -or $frontmatter -match "\n${field}:") {
                Write-Issue "ERROR" $skillMdPath "Non-standard field: '$field' (not in Agent Skills spec)"
                $script:errors++
            }
        }

        # Check allowed-tools format (should be space-delimited, not comma-separated)
        if ($frontmatter -match 'allowed-tools:\s*(.+)') {
            $toolsValue = $Matches[1].Trim()
            if ($toolsValue -match ',') {
                Write-Issue "ERROR" $skillMdPath "allowed-tools uses commas; spec requires space-delimited"
                $script:errors++
            }
        }

        # Check for known optional fields (just info)
        $knownOptional = @('license', 'compatibility', 'metadata', 'allowed-tools')
        # Claude Code extensions (valid but not portable)
        $claudeExtensions = @('disable-model-invocation', 'user-invocable', 'model', 'context', 'agent', 'hooks', 'argument-hint')

        $allLines = $frontmatter -split "`n"
        foreach ($line in $allLines) {
            if ($line -match '^(\w[\w-]*):\s') {
                $fieldName = $Matches[1]
                $knownFields = @('name', 'description') + $knownOptional + $claudeExtensions
                if ($fieldName -notin $knownFields -and $fieldName -notin $nonStandard) {
                    Write-Issue "WARN" $skillMdPath "Unknown field: '$fieldName'"
                    $script:warnings++
                }
            }
        }

        # Check body exists (content after frontmatter)
        $body = $content -replace '(?s)^---\s*\n.*?\n---\s*\n?', ''
        if ([string]::IsNullOrWhiteSpace($body)) {
            Write-Issue "WARN" $skillMdPath "No body content after frontmatter"
            $script:warnings++
        }

        # Check file size (recommend < 500 lines per spec)
        $lineCount = ($content -split "`n").Count
        if ($lineCount -gt 500) {
            Write-Issue "WARN" $skillMdPath "File exceeds 500 lines ($lineCount); consider splitting into references/"
            $script:warnings++
        }
    }
}

Write-Host ""
Write-Host "Results: " -NoNewline
if ($errors -eq 0) {
    Write-Host "$errors errors" -NoNewline -ForegroundColor Green
} else {
    Write-Host "$errors errors" -NoNewline -ForegroundColor Red
}
Write-Host ", " -NoNewline
if ($warnings -eq 0) {
    Write-Host "$warnings warnings" -NoNewline -ForegroundColor Green
} else {
    Write-Host "$warnings warnings" -NoNewline -ForegroundColor Yellow
}
Write-Host ""

if ($Strict -and ($errors -gt 0 -or $warnings -gt 0)) {
    exit 1
} elseif ($errors -gt 0) {
    exit 1
} else {
    exit 0
}
