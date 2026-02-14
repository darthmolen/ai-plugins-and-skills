<#
.SYNOPSIS
    Generates index.json and updates README.md with current plugin skills
.DESCRIPTION
    Scans all plugins for skills, extracts metadata from SKILL.md files,
    generates index.json, and updates the directory tree in README.md
#>

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PluginsDir = Join-Path $RepoRoot "claude-code\plugins"
$ReadmePath = Join-Path $RepoRoot "README.md"

# Box drawing chars as variables to avoid encoding issues
$L = [char]0x2514  # corner
$T = [char]0x251C  # tee
$V = [char]0x2502  # vertical
$H = [char]0x2500  # horizontal

$Corner = "$L$H$H "
$Tee = "$T$H$H "
$Vert = "$V   "

# Collect all plugins and skills
$plugins = @()

Get-ChildItem -Path $PluginsDir -Directory | ForEach-Object {
    $pluginDir = $_
    $pluginJsonPath = Join-Path $pluginDir.FullName ".claude-plugin\plugin.json"

    if (Test-Path $pluginJsonPath) {
        $pluginJson = Get-Content $pluginJsonPath -Raw | ConvertFrom-Json

        $skills = @()
        $skillsDir = Join-Path $pluginDir.FullName "skills"

        if (Test-Path $skillsDir) {
            Get-ChildItem -Path $skillsDir -Directory | Sort-Object Name | ForEach-Object {
                $skillDir = $_
                $skillMdPath = Join-Path $skillDir.FullName "SKILL.md"

                if (Test-Path $skillMdPath) {
                    $content = Get-Content $skillMdPath -Raw

                    # Extract name and description from frontmatter
                    if ($content -match '(?s)^---\s*\n(.*?)\n---') {
                        $frontmatter = $Matches[1]
                        $name = if ($frontmatter -match 'name:\s*(.+)') { $Matches[1].Trim() } else { $skillDir.Name }
                        $desc = if ($frontmatter -match 'description:\s*(.+)') { $Matches[1].Trim() } else { "" }

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

        $commands = @()
        $commandsDir = Join-Path $pluginDir.FullName "commands"
        if (Test-Path $commandsDir) {
            Get-ChildItem -Path $commandsDir -Filter "*.md" | ForEach-Object {
                $commands += $_.BaseName
            }
        }

        $plugins += @{
            name = $pluginJson.name
            version = $pluginJson.version
            description = $pluginJson.description
            folder = $pluginDir.Name
            skills = $skills
            commands = $commands
        }
    }
}

# Generate index.json
$index = @{
    generated = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
    plugins = $plugins
}

$indexPath = Join-Path $RepoRoot "index.json"
$index | ConvertTo-Json -Depth 10 | Set-Content $indexPath -Encoding UTF8
Write-Host "Generated: $indexPath"

# Generate directory tree for README
$lines = @()
$lines += "ai-plugins-and-skills/"
$lines += "${Tee}.claude-plugin/"
$lines += "${Vert}${Corner}marketplace.json          # Local marketplace definition"
$lines += "${Tee}README.md"
$lines += "${Tee}index.json                    # Auto-generated plugin index"
$lines += "${Tee}scripts/"
$lines += "${Vert}${Corner}claude-code/"
$lines += "${Vert}    ${Tee}install.sh            # Unix installer"
$lines += "${Vert}    ${Corner}install.cmd           # Windows installer"
$lines += "${Corner}claude-code/"
$lines += "    ${Corner}plugins/"

for ($i = 0; $i -lt $plugins.Count; $i++) {
    $plugin = $plugins[$i]
    $isLast = ($i -eq $plugins.Count - 1)
    $prefix = if ($isLast) { $Corner } else { $Tee }
    $childPrefix = if ($isLast) { "    " } else { $Vert }

    $lines += "        $prefix$($plugin.folder)/"
    $lines += "        $childPrefix${Tee}.claude-plugin/"
    $lines += "        $childPrefix${Vert}${Corner}plugin.json"

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

# Update README.md
$readme = Get-Content $ReadmePath -Raw

# Replace the directory structure section
$pattern = '(?s)(## Directory Structure\s*\n\n)```[\s\S]*?```'
$replacement = "`$1$tree"

$newReadme = $readme -replace $pattern, $replacement
$newReadme | Set-Content $ReadmePath -Encoding UTF8 -NoNewline

Write-Host "Updated: $ReadmePath"

# Also update the plugins table
$tableHeader = "| Plugin | Description | Skills |"
$tableSep = "|--------|-------------|--------|"
$tableRows = @($tableHeader, $tableSep)

foreach ($plugin in $plugins) {
    $skillNames = ($plugin.skills | ForEach-Object { $_.name }) -join ", "
    $tableRows += "| [$($plugin.name)](claude-code/plugins/$($plugin.folder)/) | $($plugin.description) | $skillNames |"
}

$tableContent = $tableRows -join "`n"

$readme = Get-Content $ReadmePath -Raw
$tablePattern = '(?s)\| Plugin \| Description \| Skills \|[\s\S]*?\n\n'
$newReadme = $readme -replace $tablePattern, "$tableContent`n`n"
$newReadme | Set-Content $ReadmePath -Encoding UTF8 -NoNewline

Write-Host "Updated plugins table in README.md"
Write-Host ""
Write-Host "Plugins found:"
foreach ($plugin in $plugins) {
    Write-Host "  - $($plugin.name) v$($plugin.version): $($plugin.skills.Count) skills, $($plugin.commands.Count) commands"
}
