<#
.SYNOPSIS
    Generates platform-specific plugin packaging from unified plugin.yaml manifests
.DESCRIPTION
    Reads plugin.yaml files as the single source of truth and generates:
    - .claude-plugin/plugin.json (Claude Code manifest)
    - .claude-plugin/marketplace.json (Claude Code marketplace, repo root)
    - .github/plugin/marketplace.json (Copilot CLI marketplace, repo root)
    - index.json (auto-generated plugin/skill index)
    - Updates documentation/ARCHITECTURE.md directory tree and README.md plugins table

    This is the unified build script for cross-platform plugin distribution.
    It absorbs the logic from generate-index.ps1 and adds Copilot CLI support.
.LINK
    https://agentskills.io/specification
    https://deepwiki.com/github/copilot-plugins
#>

param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$SkipReadme,
    [switch]$Validate
)

$PluginsDir = Join-Path $RepoRoot "claude-code\plugins"

# UTF-8 without BOM (PS 5.1's -Encoding UTF8 adds BOM, which breaks Copilot CLI JSON parsing)
$Utf8NoBom = New-Object System.Text.UTF8Encoding $false

# Run validation first if requested
if ($Validate) {
    $validateScript = Join-Path $PSScriptRoot "validate-skills.ps1"
    & $validateScript -PluginsDir $PluginsDir
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Validation failed. Fix errors before building." -ForegroundColor Red
        exit 1
    }
    Write-Host ""
}

# Box drawing chars
$L = [char]0x2514
$T = [char]0x251C
$V = [char]0x2502
$H = [char]0x2500
$Corner = "$L$H$H "
$Tee = "$T$H$H "
$Vert = "$V   "

# ── Marketplace metadata ──────────────────────────────────────────────
$marketplaceName = "ai-plugins-and-skills-ai-standards"
$marketplaceDescription = "ai-plugins-and-skills coding standards and tools for Claude Code and Copilot CLI"
$ownerName = "ai-plugins-and-skills IT"

# ── Scan plugins ──────────────────────────────────────────────────────
$plugins = @()

Get-ChildItem -Path $PluginsDir -Directory | ForEach-Object {
    $pluginDir = $_

    # Prefer plugin.yaml, fall back to plugin.json
    $pluginYamlPath = Join-Path $pluginDir.FullName "plugin.yaml"
    $pluginJsonPath = Join-Path $pluginDir.FullName ".claude-plugin\plugin.json"

    $pluginMeta = $null

    if (Test-Path $pluginYamlPath) {
        # Parse YAML manually (basic key: value parsing, no module dependency)
        $yamlContent = Get-Content $pluginYamlPath -Raw

        # Extract fields individually to avoid $Matches pollution in hashtable init
        $pName = if ($yamlContent -match '(?m)^name:\s*(.+)') { $Matches[1].Trim().Trim('"') } else { $pluginDir.Name }
        $pVersion = if ($yamlContent -match '(?m)^version:\s*(.+)') { $Matches[1].Trim().Trim('"') } else { "0.0.0" }
        $pDesc = if ($yamlContent -match '(?m)^description:\s*"?([^"]+)"?') { $Matches[1].Trim() } else { "" }
        $pLicense = if ($yamlContent -match '(?m)^license:\s*(.+)') { $Matches[1].Trim() } else { $null }
        # Match author name: indented 'name:' after 'author:' line
        $pAuthor = if ($yamlContent -match '(?m)^author:\s*\r?\n[ \t]+name:\s*([^\r\n]+)') { $Matches[1].Trim() } else { $ownerName }

        $pluginMeta = @{
            name = $pName
            version = $pVersion
            description = $pDesc
            license = $pLicense
            authorName = $pAuthor
        }
    } elseif (Test-Path $pluginJsonPath) {
        $json = Get-Content $pluginJsonPath -Raw | ConvertFrom-Json
        $pluginMeta = @{
            name = $json.name
            version = $json.version
            description = $json.description
            license = $null
            authorName = if ($json.author) { $json.author.name } else { $ownerName }
        }
    } else {
        Write-Host "SKIP: $($pluginDir.Name) (no plugin.yaml or plugin.json)" -ForegroundColor Yellow
        return
    }

    # Scan skills
    $skills = @()
    $skillsDir = Join-Path $pluginDir.FullName "skills"

    if (Test-Path $skillsDir) {
        Get-ChildItem -Path $skillsDir -Directory | Sort-Object Name | ForEach-Object {
            $skillDir = $_
            $skillMdPath = Join-Path $skillDir.FullName "SKILL.md"

            if (Test-Path $skillMdPath) {
                $content = Get-Content $skillMdPath -Raw

                if ($content -match '(?s)^---\s*\n(.*?)\n---') {
                    $frontmatter = $Matches[1]
                    $name = if ($frontmatter -match 'name:\s*(.+)') { $Matches[1].Trim().Trim('"').Trim("'") } else { $skillDir.Name }
                    $desc = if ($frontmatter -match 'description:\s*"?([^"]+)"?') { $Matches[1].Trim() } else { "" }

                    # Truncate description to first sentence for index
                    $shortDesc = ($desc -split '\. ')[0]
                    if ($shortDesc.Length -gt 100) {
                        $shortDesc = $shortDesc.Substring(0, 97) + "..."
                    }

                    $skills += @{
                        name = $name
                        folder = $skillDir.Name
                        description = $shortDesc
                    }
                }
            }
        }
    }

    # Scan commands
    $commands = @()
    $commandsDir = Join-Path $pluginDir.FullName "commands"
    if (Test-Path $commandsDir) {
        Get-ChildItem -Path $commandsDir -Filter "*.md" | ForEach-Object {
            $commands += $_.BaseName
        }
    }

    $plugins += @{
        name = $pluginMeta.name
        version = $pluginMeta.version
        description = $pluginMeta.description
        license = $pluginMeta.license
        authorName = $pluginMeta.authorName
        folder = $pluginDir.Name
        skills = $skills
        commands = $commands
        source = "./claude-code/plugins/$($pluginDir.Name)"
    }

    # ── Generate .claude-plugin/plugin.json from plugin.yaml ──
    if (Test-Path $pluginYamlPath) {
        $claudePluginDir = Join-Path $pluginDir.FullName ".claude-plugin"
        if (-not (Test-Path $claudePluginDir)) {
            New-Item -ItemType Directory -Path $claudePluginDir -Force | Out-Null
        }

        $claudePluginJson = @{
            name = $pluginMeta.name
            version = $pluginMeta.version
            description = $pluginMeta.description
            author = @{ name = $pluginMeta.authorName }
        }

        $claudePluginJsonPath = Join-Path $claudePluginDir "plugin.json"
        [System.IO.File]::WriteAllText($claudePluginJsonPath, ($claudePluginJson | ConvertTo-Json -Depth 5), $Utf8NoBom)
    }
}

# ── Generate index.json ───────────────────────────────────────────────
$index = @{
    generated = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
    plugins = $plugins | ForEach-Object {
        @{
            name = $_.name
            version = $_.version
            description = $_.description
            folder = $_.folder
            skills = $_.skills
            commands = $_.commands
        }
    }
}

$indexPath = Join-Path $RepoRoot "index.json"
[System.IO.File]::WriteAllText($indexPath, ($index | ConvertTo-Json -Depth 10), $Utf8NoBom)
Write-Host "Generated: $indexPath" -ForegroundColor Green

# ── Generate Claude Code marketplace.json ─────────────────────────────
$claudeMarketplace = @{
    name = $marketplaceName
    description = $marketplaceDescription
    owner = @{ name = $ownerName }
    plugins = $plugins | ForEach-Object {
        @{
            name = $_.name
            description = $_.description
            version = $_.version
            source = $_.source
            author = @{ name = $_.authorName }
        }
    }
}

$claudeMarketplacePath = Join-Path $RepoRoot ".claude-plugin\marketplace.json"
$claudeMarketplaceDir = Split-Path $claudeMarketplacePath -Parent
if (-not (Test-Path $claudeMarketplaceDir)) {
    New-Item -ItemType Directory -Path $claudeMarketplaceDir -Force | Out-Null
}
[System.IO.File]::WriteAllText($claudeMarketplacePath, ($claudeMarketplace | ConvertTo-Json -Depth 5), $Utf8NoBom)
Write-Host "Generated: $claudeMarketplacePath" -ForegroundColor Green

# ── Generate Copilot CLI marketplace.json ─────────────────────────────
$copilotMarketplace = @{
    name = $marketplaceName
    metadata = @{
        description = $marketplaceDescription
        version = "1.0.0"
    }
    owner = @{
        name = $ownerName
    }
    plugins = $plugins | ForEach-Object {
        $plugin = $_
        $skillPaths = @($plugin.skills | ForEach-Object { "./skills/$($_.folder)" })
        @{
            name = $plugin.name
            source = $plugin.source
            description = $plugin.description
            version = $plugin.version
            skills = $skillPaths
        }
    }
}

$copilotMarketplacePath = Join-Path $RepoRoot ".github\plugin\marketplace.json"
$copilotMarketplaceDir = Split-Path $copilotMarketplacePath -Parent
if (-not (Test-Path $copilotMarketplaceDir)) {
    New-Item -ItemType Directory -Path $copilotMarketplaceDir -Force | Out-Null
}
[System.IO.File]::WriteAllText($copilotMarketplacePath, ($copilotMarketplace | ConvertTo-Json -Depth 5), $Utf8NoBom)
Write-Host "Generated: $copilotMarketplacePath" -ForegroundColor Green

# ── Update ARCHITECTURE.md (directory tree) ──────────────────────────
if (-not $SkipReadme) {
    $ArchPath = Join-Path $RepoRoot "documentation\ARCHITECTURE.md"

    if (Test-Path $ArchPath) {
        # Generate directory tree
        $lines = @()
        $lines += "ai-plugins-and-skills/"
        $lines += "${Tee}.claude-plugin/"
        $lines += "${Vert}${Corner}marketplace.json          # Claude Code marketplace"
        $lines += "${Tee}.github/"
        $lines += "${Vert}${Corner}plugin/"
        $lines += "${Vert}    ${Corner}marketplace.json      # Copilot CLI marketplace"
        $lines += "${Tee}README.md"
        $lines += "${Tee}index.json                    # Auto-generated plugin index"
        $lines += "${Tee}scripts/"
        $lines += "${Vert}${Tee}build-plugin.ps1          # Cross-platform build"
        $lines += "${Vert}${Tee}validate-skills.ps1       # Agent Skills spec validator"
        $lines += "${Vert}${Corner}generate-index.ps1        # Legacy (calls build-plugin)"
        $lines += "${Corner}claude-code/"
        $lines += "    ${Corner}plugins/"

        for ($i = 0; $i -lt $plugins.Count; $i++) {
            $plugin = $plugins[$i]
            $isLast = ($i -eq $plugins.Count - 1)
            $prefix = if ($isLast) { $Corner } else { $Tee }
            $childPrefix = if ($isLast) { "    " } else { $Vert }

            $lines += "        $prefix$($plugin.folder)/"
            $lines += "        $childPrefix${Tee}plugin.yaml               # Source of truth"
            $lines += "        $childPrefix${Tee}.claude-plugin/"
            $lines += "        $childPrefix${Vert}${Corner}plugin.json            # Generated"

            if ($plugin.commands.Count -gt 0) {
                $lines += "        $childPrefix${Tee}commands/"
                for ($j = 0; $j -lt $plugin.commands.Count; $j++) {
                    $cmd = $plugin.commands[$j]
                    $cmdPrefix = if ($j -eq $plugin.commands.Count - 1) { $Corner } else { $Tee }
                    $lines += "        $childPrefix${Vert}$cmdPrefix$cmd.md"
                }
            }

            if ($plugin.skills.Count -gt 0) {
                $lines += "        $childPrefix${Corner}skills/"
                for ($j = 0; $j -lt $plugin.skills.Count; $j++) {
                    $skill = $plugin.skills[$j]
                    $skillPrefix = if ($j -eq $plugin.skills.Count - 1) { $Corner } else { $Tee }
                    $skillChildPrefix = if ($j -eq $plugin.skills.Count - 1) { "    " } else { $Vert }
                    $lines += "        $childPrefix    $skillPrefix$($skill.folder)/"
                    $lines += "        $childPrefix    $skillChildPrefix${Corner}SKILL.md"
                }
            }
        }

        $tree = '```' + "`n" + ($lines -join "`n") + "`n" + '```'

        $arch = Get-Content $ArchPath -Raw

        $pattern = '(?s)(## Directory Structure\s*\n\n)```[\s\S]*?```'
        $replacement = "`$1$tree"
        $newArch = $arch -replace $pattern, $replacement
        [System.IO.File]::WriteAllText($ArchPath, $newArch, $Utf8NoBom)

        Write-Host "Updated: $ArchPath (directory tree)" -ForegroundColor Green
    }

    # ── Update README.md (plugins table) ─────────────────────────────
    $ReadmePath = Join-Path $RepoRoot "README.md"

    if (Test-Path $ReadmePath) {
        $tableHeader = "| Plugin | Description | Skills |"
        $tableSep = "|--------|-------------|--------|"
        $tableRows = @($tableHeader, $tableSep)

        foreach ($plugin in $plugins) {
            $skillNames = ($plugin.skills | ForEach-Object { $_.name }) -join ", "
            $tableRows += "| [$($plugin.name)](claude-code/plugins/$($plugin.folder)/) | $($plugin.description) | $skillNames |"
        }

        $tableContent = $tableRows -join "`n"

        $readme = Get-Content $ReadmePath -Raw
        $tablePattern = '(?s)\| Plugin \| Description \| Skills\s*\|[\s\S]*?\n\n'
        $newReadme = $readme -replace $tablePattern, "$tableContent`n`n"
        [System.IO.File]::WriteAllText($ReadmePath, $newReadme, $Utf8NoBom)

        Write-Host "Updated: $ReadmePath (plugins table)" -ForegroundColor Green
    }
}

# ── Summary ───────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Build complete!" -ForegroundColor Cyan
Write-Host "Platforms: Claude Code, Copilot CLI, VS Code Copilot" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Plugins:" -ForegroundColor White
foreach ($plugin in $plugins) {
    Write-Host "  - $($plugin.name) v$($plugin.version): $($plugin.skills.Count) skills, $($plugin.commands.Count) commands"
}
Write-Host ""
Write-Host "Generated files:" -ForegroundColor White
Write-Host "  - index.json"
Write-Host "  - .claude-plugin/marketplace.json (Claude Code)"
Write-Host "  - .github/plugin/marketplace.json (Copilot CLI)"
foreach ($plugin in $plugins) {
    Write-Host "  - claude-code/plugins/$($plugin.folder)/.claude-plugin/plugin.json"
}
