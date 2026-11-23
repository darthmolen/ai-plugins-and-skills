#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Names a wave session after the role its first prompt starts (ai-plugins-and-skills:wave-workflow).

.DESCRIPTION
    Runs as a Claude Code prompt hook. When the prompt is exactly
    `/ai-plugins-and-skills:wave-workflow lead` or `/ai-plugins-and-skills:wave-workflow track <name>`, it finds
    planning/WAVE-README.md (in the session's working directory or any parent), reads the
    wave-launch block that plan-doc.cs generates there, and returns the session's name as
    hookSpecificOutput.sessionTitle. Claude Code records that as the session's title and its agent
    name, the name the lead messages it by.

    Anything else (another prompt, no open wave, two open waves, a track the wave does not have,
    input it cannot read) produces no output and exit 0. It never blocks a prompt.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $hookInput = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $prompt = [string] $hookInput.prompt
    $m = [regex]::Match($prompt, '^\s*/ai-plugins-and-skills:wave-workflow\s+(?:(?<lead>lead)|track\s+(?<track>[a-z0-9][a-z0-9-]*))\s*$')
    if (-not $m.Success) {
        exit 0
    }

    $track = if ($m.Groups['lead'].Success) { 'lead' } else { $m.Groups['track'].Value }

    $dir = [string] $hookInput.cwd
    $sign = $null
    while ($dir) {
        $candidate = Join-Path $dir 'planning' 'WAVE-README.md'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $sign = $candidate
            break
        }

        $dir = Split-Path -Parent $dir
    }

    if (-not $sign) {
        exit 0
    }

    $blocks = @([regex]::Matches((Get-Content -LiteralPath $sign -Raw), '(?m)^<!-- wave-launch (\{.*\}) -->\r?$'))
    if ($blocks.Count -ne 1) {
        exit 0
    }

    $session = @(($blocks[0].Groups[1].Value | ConvertFrom-Json).sessions | Where-Object { $_.track -ceq $track })
    if ($session.Count -ne 1) {
        exit 0
    }

    $eventName = if ($hookInput.PSObject.Properties['hook_event_name']) { [string] $hookInput.hook_event_name } else { 'UserPromptSubmit' }
    @{ hookSpecificOutput = @{ hookEventName = $eventName; sessionTitle = [string] $session[0].name } } | ConvertTo-Json -Compress
}
catch {
    # A naming hook that fails must not cost anyone their prompt.
}

exit 0
