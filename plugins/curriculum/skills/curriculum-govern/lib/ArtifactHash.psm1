# ArtifactHash - the single implementation of the curriculum artifact content_hash.
#
# TWO CONSUMERS, ONE ALGORITHM, DELIBERATELY:
#   scripts/hash-artifact.ps1        computes digests to record in a manifest
#   scripts/validate-curriculum.ps1  gate artifact_hashes_reproduce, recomputes and compares
#
# A second implementation is how this repository ended up with two incompatible families of
# "ooxml-stable" digest - the decks hashed with a NUL separator, the documents without - under
# one algorithm name. Do not add a third. If the algorithm must change, change it here.
#
# Full rules and rationale: learning/agents/artifact-generation-agent.md#content-hashing

Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$script:OoxmlExtensions = @('.docx', '.pptx', '.xlsx')

<#
.SYNOPSIS
    True when git will check this repository out with CRLF line endings.
.DESCRIPTION
    Text digests are taken over working-tree bytes, so on such a repository a digest computed
    over LF bytes will not reproduce after a fresh clone. Callers use this to decide whether
    -Crlf is required rather than optional.
#>
function Test-CrlfCheckout {
    [CmdletBinding()]
    param()

    $autocrlf = (git config core.autocrlf 2>$null)
    $eol      = (git config core.eol 2>$null)
    return ($autocrlf -eq 'true') -or ($eol -eq 'crlf')
}

<#
.SYNOPSIS
    Normalizes a byte array to CRLF line endings without touching anything else.
.DESCRIPTION
    Byte-level on purpose. Round-tripping through a string and re-encoding would silently drop a
    byte-order mark and impose an encoding choice; the digest must be over the bytes the file
    actually has. CRLF -> LF first so mixed input converges, then LF -> CRLF.

    Assumes a byte-oriented encoding (UTF-8, ASCII, Latin-1). A UTF-16 artifact would be
    corrupted by this, and none exist in this curriculum.
#>
function Convert-ToCrlfBytes {
    [CmdletBinding()]
    param([Parameter(Mandatory)][byte[]] $Bytes)

    $out = [System.Collections.Generic.List[byte]]::new($Bytes.Length + 64)
    for ($i = 0; $i -lt $Bytes.Length; $i++) {
        $b = $Bytes[$i]
        if ($b -eq 0x0D) {
            # Drop a CR that introduces a CRLF; the LF below re-emits the pair. A lone CR
            # (classic Mac) is left alone rather than guessed at.
            if (($i + 1) -lt $Bytes.Length -and $Bytes[$i + 1] -eq 0x0A) { continue }
            $out.Add($b)
            continue
        }
        if ($b -eq 0x0A) { $out.Add(0x0D); $out.Add(0x0A); continue }
        $out.Add($b)
    }
    return $out.ToArray()
}

<#
.SYNOPSIS
    The ooxml-stable digest of a .docx / .pptx / .xlsx.
.DESCRIPTION
    SHA-256 over sorted (part name, NUL, part bytes), excluding docProps/core.xml.

    Plain SHA-256 over the file is NOT stable: pandoc and pptxgenjs both embed render timestamps
    in docProps/core.xml, and zip entry mtimes vary, so two renders of identical input differ
    byte-for-byte while every document part is identical.

    FOUR DETAILS ARE PART OF THE ALGORITHM. Each one changes the digest:
      1. ORDINAL sort of part names - Python's sorted(). A culture-aware sort orders
         '[Content_Types].xml' and '_rels/...' differently.
      2. UTF-8 encoded part names, hashed before their content.
      3. A NUL byte between name and content.
      4. Directory entries INCLUDED - name + NUL, no content bytes - because they appear in
         the zip name list.

    Details 1 and 4 were underspecified until 2026-08-17 and were recovered by re-deriving the
    recorded ai-121 pptx digest from the committed deck.
#>
function Get-OoxmlStableHash {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $Path)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path))
    try {
        $names = [System.Collections.Generic.List[string]]::new()
        foreach ($entry in $zip.Entries) { $names.Add($entry.FullName) }
        $names.Sort([System.StringComparer]::Ordinal)

        $ms = New-Object System.IO.MemoryStream
        foreach ($name in $names) {
            if ($name -eq 'docProps/core.xml') { continue }

            $nameBytes = [System.Text.Encoding]::UTF8.GetBytes($name)
            $ms.Write($nameBytes, 0, $nameBytes.Length)
            $ms.WriteByte(0)

            $stream = $zip.GetEntry($name).Open()
            try { $stream.CopyTo($ms) } finally { $stream.Dispose() }
        }
        return 'ooxml-stable:sha256:' + [System.BitConverter]::ToString($sha.ComputeHash($ms.ToArray())).Replace('-', '').ToLower()
    }
    finally {
        $zip.Dispose()
        $sha.Dispose()
    }
}

<#
.SYNOPSIS
    Computes the manifest content_hash and byte count for one artifact. Never writes.
.PARAMETER Crlf
    Compute the digest over CRLF-normalized bytes rather than the bytes on disk. Required on a
    repository that checks out CRLF; ignored for OOXML artifacts.

    The returned byte count is of the NORMALIZED bytes, which is what makes the gate stable: a
    working tree holding LF and a fresh clone holding CRLF agree on both fields.
#>
function Get-ArtifactContentHash {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [switch] $Crlf
    )

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($script:OoxmlExtensions -contains $item.Extension.ToLower()) {
        return [pscustomobject]@{
            path         = $Path
            algorithm    = 'ooxml-stable'
            content_hash = Get-OoxmlStableHash -Path $item.FullName
            bytes        = $item.Length
        }
    }

    $bytes = [System.IO.File]::ReadAllBytes($item.FullName)
    if ($Crlf) { $bytes = Convert-ToCrlfBytes -Bytes $bytes }

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hex = [System.BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLower()
        return [pscustomobject]@{
            path         = $Path
            algorithm    = 'sha256'
            content_hash = 'sha256:' + $hex
            bytes        = $bytes.Length
        }
    }
    finally { $sha.Dispose() }
}

<#
.SYNOPSIS
    Rewrites a text artifact to CRLF in place. Returns $true when the file changed.
.DESCRIPTION
    For generation, not verification - the gate must never mutate what it inspects. Byte-level,
    so a byte-order mark survives.
#>
function Repair-ArtifactLineEnding {
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)][string] $Path)

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($script:OoxmlExtensions -contains $item.Extension.ToLower()) { return $false }

    $original   = [System.IO.File]::ReadAllBytes($item.FullName)
    $normalized = Convert-ToCrlfBytes -Bytes $original

    # Plain comparison on purpose: [Linq.Enumerable]::SequenceEqual is a generic method and
    # PowerShell will not bind it from a byte[] pair.
    if ($original.Length -eq $normalized.Length) {
        $same = $true
        for ($i = 0; $i -lt $original.Length; $i++) {
            if ($original[$i] -ne $normalized[$i]) { $same = $false; break }
        }
        if ($same) { return $false }
    }

    if ($PSCmdlet.ShouldProcess($item.FullName, 'Normalize line endings to CRLF')) {
        [System.IO.File]::WriteAllBytes($item.FullName, $normalized)
        return $true
    }
    return $false
}

Export-ModuleMember -Function Test-CrlfCheckout, Convert-ToCrlfBytes, Get-OoxmlStableHash,
                              Get-ArtifactContentHash, Repair-ArtifactLineEnding
