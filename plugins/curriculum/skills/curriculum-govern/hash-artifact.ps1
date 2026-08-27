#Requires -Version 7.0
<#
.SYNOPSIS
    Computes the content_hash recorded in curriculum generation manifests.

.DESCRIPTION
    The algorithm lives in lib/ArtifactHash.psm1 and is shared with
    validate-curriculum.ps1's artifact_hashes_reproduce gate, so the value recorded and the value
    verified cannot drift apart. This script is the recording front end.

    It exists at all because it was once an ad-hoc script that was NOT preserved: a recorded pptx
    digest later failed to reproduce and the ambiguity turned out to be in the framing of the
    digest, not in the file. Do not recompute these digests by hand.

    TEXT artifacts (.md, .yaml, .json)
      Plain SHA-256 over the working-tree bytes, so the digest IS line-ending sensitive.
      With git configured to check out CRLF - core.autocrlf=true, core.eol=crlf, or a
      .gitattributes 'text eol=crlf' - a file written LF in the working tree is committed as LF
      and checked out as CRLF, so a hash taken over the LF bytes will not reproduce on the next
      clone. -Normalize rewrites the file to CRLF first, which is what the generation contract
      requires on such a repository.

    OOXML artifacts (.docx, .pptx)
      Algorithm 'ooxml-stable' - see the module for the four framing details that are part of it.

.PARAMETER Path
    One or more artifact paths.

.PARAMETER Normalize
    Rewrite text artifacts to CRLF before hashing. Required on a repository that checks out CRLF;
    harmless where the file is already CRLF. Ignored for OOXML artifacts. Use -WhatIf to see what
    would be rewritten without touching anything.

.PARAMETER Json
    Emit manifest-shaped JSON instead of a table.

.EXAMPLE
    .\scripts\hash-artifact.ps1 learning/generated/ai-121/*.md -Normalize
    .\scripts\hash-artifact.ps1 learning/generated/ai-121/presentation.pptx
    .\scripts\hash-artifact.ps1 learning/generated/ai-101 -Normalize -Json
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory, Position = 0, ValueFromPipeline)]
    [string[]] $Path,

    [switch] $Normalize,

    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'lib/ArtifactHash.psm1') -Force

$results  = [System.Collections.Generic.List[object]]::new()
$repaired = [System.Collections.Generic.List[string]]::new()

foreach ($p in $Path) {
    foreach ($item in (Get-Item -Path $p -ErrorAction Stop)) {
        $files = if ($item.PSIsContainer) { Get-ChildItem -LiteralPath $item.FullName -File } else { @($item) }

        foreach ($file in $files) {
            if ($Normalize) {
                if (Repair-ArtifactLineEnding -Path $file.FullName) { $repaired.Add($file.Name) }
            }
            $result = Get-ArtifactContentHash -Path $file.FullName -Crlf:$Normalize
            $result.path = (Resolve-Path -LiteralPath $file.FullName -Relative).Replace('\', '/').TrimStart('./')
            $results.Add($result)
        }
    }
}

if ($repaired.Count -gt 0) {
    Write-Host "Normalized to CRLF: $($repaired -join ', ')" -ForegroundColor Cyan
}

if (-not $Normalize -and (Test-CrlfCheckout) -and ($results | Where-Object algorithm -eq 'sha256')) {
    Write-Warning ("This repository checks out CRLF but -Normalize was not passed. A text digest " +
                   "taken over LF bytes will not reproduce after a fresh clone. Re-run with -Normalize " +
                   "before recording these values in a manifest.")
}

if ($Json) { $results | ConvertTo-Json -Depth 3 } else { $results | Format-Table -AutoSize }
