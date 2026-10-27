#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Verifies that every quotation a generated curriculum artifact presents as verbatim is verbatim.

.DESCRIPTION
    The claims_supported gate, made deterministic for the one failure mode a regex can reach: a
    quotation that has been trimmed, elided or re-punctuated and is still presented as an exact
    quote of a cited source.

    This does NOT replace the Governance Agent. It cannot tell whether a source says what a course
    claims it says — that is the DATA-201 failure class and it stays a judgement gate. It catches
    only the mechanical case, which is worth catching because eight instances of it shipped into
    a production curriculum's courses and every one would have survived a read-through.

    WHAT COUNTS AS A QUOTATION

    Markdown artifacts overload `*"..."*`: it wraps both real citations and invented instructor
    dialogue ("I never checked." — a thing to say to a room). Measured over the real corpus, 20 of
    65 quotations are dialogue, so a checker that reports them all is one nobody leaves switched on.

    The discriminator is the [S] / [P] marker the artifacts already carry, which was chosen over an
    attribution-link heuristic because the data refused the latter: 37 real citations carry no
    adjacent link. Markers win because they already encode the needed distinction — [S] claims
    source-derived, [P] admits invented — so a quotation under [S] that is absent from the sources
    is a defect by the artifact's own claim rather than by this script's guess.

      [S] context  ->  must appear verbatim in one of the course's cited sources
      [P] context  ->  skipped, invented by definition
      no marker    ->  skipped, and counted in the summary so the silence is visible

    Slide outlines mark provenance per slide on a **Source:** line rather than inline; that form is
    read too, or every slide quotation would report as unmarked.

    ELISIONS ARE LEGAL WHEN MARKED. "..." or the ellipsis character splits a quotation into
    fragments, each checked independently. That is the whole point: an honest trim passes, a silent
    one fails.

    YAML artifacts need no heuristic. quiz.yaml already carries structured citations —
    `evidence: {source, quote}` — which is why the quiz passed on the first hand run while the
    markdown did not. Each quote is checked against the file its own source names.

.PARAMETER Path
    Directory of generated artifacts to scan. Defaults to learning/generated under RepoRoot.

.PARAMETER RepoRoot
    Curriculum workspace root that source paths resolve against. Defaults to the current directory.

.PARAMETER Quiet
    Suppress per-quotation output; print only the summary and any violations.

.EXAMPLE
    .\check-quotations.ps1
    .\check-quotations.ps1 -Path learning/generated/ai-121
#>
[CmdletBinding()]
param(
    [string] $Path,
    [string] $RepoRoot,
    [switch] $Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) { $RepoRoot = (Get-Location).Path }
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
if (-not $Path) { $Path = Join-Path $RepoRoot 'learning' 'generated' }

$script:Violations = [System.Collections.Generic.List[pscustomobject]]::new()
$script:Checked = 0
$script:Skipped = 0
$script:Unmarked = 0

function ConvertTo-Comparable {
    <#
        Normalize away everything that is presentation rather than wording:
        emphasis and code marks, quote and dash variants, and whitespace/line wrapping.

        It deliberately does NOT normalize away punctuation that ends a sentence. A full stop
        standing where the source has a comma is exactly one of the defects this exists to catch,
        so forgiving it would defeat the gate.
    #>
    param([Parameter(Mandatory)][AllowEmptyString()][string] $Text)

    # Delimiters and emphasis, including the ASCII apostrophe: a course nesting a source's
    # *"quote"* inside its own switches to 'single quotes', and that is delimiter style rather
    # than wording. Dropping it also folds "it's"/"its" together, which is a deliberate and
    # narrow over-forgiveness - no observed defect turned on an apostrophe.
    $t = $Text -replace '[*`_"''‘’“”]', ''
    $t = $t -replace '[–—]', '-'
    $t = $t -replace '…', '...'
    $t = $t -replace '\s+', ' '

    # A dash used as a SEPARATOR carries no wording. Sources render "### Question? - **Answer.**"
    # as a heading and courses legitimately quote it as prose. Only a dash flanked by spaces is
    # dropped, so hyphenated words and "-> " arrows survive intact.
    $t = $t -replace '\s-\s', ' '
    $t = $t -replace '\s+', ' '

    return $t.Trim().ToLowerInvariant()
}

function Get-CourseCorpus {
    <# The concatenated, normalized text of every source a course cites. #>
    param([Parameter(Mandatory)][string] $CourseId)

    $yaml = Join-Path $RepoRoot 'learning' 'courses' $CourseId 'course.yaml'
    if (-not (Test-Path -LiteralPath $yaml)) { return $null }

    $paths = [System.Collections.Generic.List[string]]::new()
    $inBlock = $false
    foreach ($line in (Get-Content -LiteralPath $yaml)) {
        if ($line -match '^source_locations:') { $inBlock = $true; continue }
        if ($inBlock -and $line -match '^[a-z_]+:') { $inBlock = $false }
        if ($inBlock -and $line -match '^\s*path:\s*(\S.*?)\s*$') { $paths.Add($Matches[1]) }
    }

    $sb = [System.Text.StringBuilder]::new()
    foreach ($p in $paths) {
        $full = Join-Path $RepoRoot $p
        if (-not (Test-Path -LiteralPath $full)) { continue }
        $item = Get-Item -LiteralPath $full
        $files = if ($item.PSIsContainer) {
            Get-ChildItem -LiteralPath $full -Recurse -File -Include *.md, *.yaml, *.json -ErrorAction SilentlyContinue
        }
        else { @($item) }
        foreach ($f in $files) {
            [void]$sb.Append((Get-Content -LiteralPath $f.FullName -Raw -ErrorAction SilentlyContinue))
            [void]$sb.Append(' ')
        }
    }
    # The course definition is itself quotable - instructor notes appear on slides.
    [void]$sb.Append((Get-Content -LiteralPath $yaml -Raw))
    return ConvertTo-Comparable -Text $sb.ToString()
}

function Test-Quotation {
    <#
        True when every fragment of the quotation appears in the corpus. An unmarked elision
        therefore fails, because the quotation is one fragment that spans a gap the source does
        not have; a marked one passes, because each side is checked on its own.
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string] $Quote,
        [Parameter(Mandatory)][AllowEmptyString()][string] $Corpus
    )

    foreach ($fragment in (ConvertTo-Comparable -Text $Quote) -split '\.\.\.') {
        $f = $fragment.Trim()
        if (-not $f) { continue }
        if ($Corpus -notlike "*$f*") { return $false }
    }
    return $true
}

function Get-MarkerAt {
    <#
        The provenance marker governing a quotation at $Index.

        Inline markers win: the nearest preceding `[S]` or `[P]`. Slide outlines put the marker on
        a **Source:** line AFTER the claim, so when no inline marker precedes the quotation the
        enclosing slide's Source line is consulted instead.
    #>
    param(
        [Parameter(Mandatory)][string] $Text,
        [Parameter(Mandatory)][int] $Index,
        [Parameter(Mandatory)][bool] $IsSlideOutline
    )

    $before = $Text.Substring(0, $Index)
    $inline = [regex]::Matches($before, '`\[([SP])\]`')
    if ($inline.Count -gt 0) { return $inline[$inline.Count - 1].Groups[1].Value }

    if ($IsSlideOutline) {
        # Bound the search to this slide: from the last "## " heading before the quote, forward.
        $headings = [regex]::Matches($before, '(?m)^##\s')
        $slideStart = if ($headings.Count -gt 0) { $headings[$headings.Count - 1].Index } else { 0 }
        $rest = $Text.Substring($slideStart)
        $nextHeading = [regex]::Match($rest.Substring(1), '(?m)^##\s')
        $slideText = if ($nextHeading.Success) { $rest.Substring(0, $nextHeading.Index + 1) } else { $rest }
        $src = [regex]::Match($slideText, '(?m)^\*\*Source:\*\*\s*`\[([SP])\]`')
        if ($src.Success) { return $src.Groups[1].Value }
    }
    return '?'
}

function Get-LineNumber {
    param([Parameter(Mandatory)][string] $Text, [Parameter(Mandatory)][int] $Index)
    return ($Text.Substring(0, $Index) -split "`n").Count
}

# ---------------------------------------------------------------- markdown
foreach ($file in (Get-ChildItem -LiteralPath $Path -Recurse -File -Filter *.md -ErrorAction SilentlyContinue)) {
    $courseId = Split-Path -Leaf (Split-Path -Parent $file.FullName)
    $corpus = Get-CourseCorpus -CourseId $courseId
    if ($null -eq $corpus) { continue }

    $text = Get-Content -LiteralPath $file.FullName -Raw
    $isSlides = $file.Name -like '*slide-outline*'

    foreach ($m in [regex]::Matches($text, '\*"(.+?)"\*', 'Singleline')) {
        $marker = Get-MarkerAt -Text $text -Index $m.Index -IsSlideOutline $isSlides
        if ($marker -eq 'P') { $script:Skipped++; continue }
        if ($marker -eq '?') { $script:Unmarked++; continue }

        $script:Checked++
        if (-not (Test-Quotation -Quote $m.Groups[1].Value -Corpus $corpus)) {
            $excerpt = (ConvertTo-Comparable -Text $m.Groups[1].Value)
            if ($excerpt.Length -gt 90) { $excerpt = $excerpt.Substring(0, 90) + '...' }
            $script:Violations.Add([pscustomobject]@{
                    File = $file.FullName.Substring($RepoRoot.Length + 1).Replace('\', '/')
                    Line = Get-LineNumber -Text $text -Index $m.Index
                    Kind = 'markdown'
                    Quote = $excerpt
                })
        }
    }
}

# ---------------------------------------------------------------- yaml evidence blocks
foreach ($file in (Get-ChildItem -LiteralPath $Path -Recurse -File -Filter *.yaml -ErrorAction SilentlyContinue)) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    $lines = $text -split "`r?`n"

    # evidence:\n  source: <path>\n  quote: >-\n    <folded block>
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch '^\s*evidence:\s*$') { continue }
        $indent = ($lines[$i] -replace '\S.*$', '').Length
        $src = $null; $quoteLines = [System.Collections.Generic.List[string]]::new(); $inQuote = $false; $hasBasis = $false
        $qid = '?'
        for ($k = $i - 1; $k -ge 0 -and $k -gt $i - 40; $k--) {
            if ($lines[$k] -match '^\s*-?\s*id:\s*(\S+)') { $qid = $Matches[1]; break }
        }
        for ($j = $i + 1; $j -lt $lines.Count; $j++) {
            $cur = $lines[$j]
            if ($cur.Trim() -and (($cur -replace '\S.*$', '').Length -le $indent)) { break }
            if ($cur -match '^\s*source:\s*(\S.*?)\s*$') { $src = $Matches[1]; $inQuote = $false; continue }
            if ($cur -match '^\s*quote:\s*[>|]-?\s*$') { $inQuote = $true; continue }
            if ($cur -match '^\s*quote:\s*(\S.*?)\s*$') { $quoteLines.Add($Matches[1]); $inQuote = $false; continue }
            # `basis:` is the YAML counterpart of [P] - evidence the course AUTHORED from the
            # source rather than lifted from it. Its wording is not checked, but the fact that it
            # names a source still is: an unverifiable provenance claim is the thing being
            # prevented, and a synthesis makes one just as a quotation does.
            if ($cur -match '^\s*basis:\s*[>|]-?\s*$') { $hasBasis = $true; $inQuote = $false; continue }
            if ($cur -match '^\s*basis:\s*(\S.*?)\s*$') { $hasBasis = $true; $inQuote = $false; continue }
            if ($inQuote) { $quoteLines.Add($cur.Trim()) }
        }
        $i = $j - 1
        if (-not $src) { continue }
        if ($quoteLines.Count -eq 0 -and -not $hasBasis) { continue }

        $script:Checked++
        $srcFull = Join-Path $RepoRoot $src
        $rel = $file.FullName.Substring($RepoRoot.Length + 1).Replace('\', '/')
        if (-not (Test-Path -LiteralPath $srcFull)) {
            $script:Violations.Add([pscustomobject]@{
                    File = $rel; Line = $i + 1; Kind = 'yaml'
                    Quote = "$qid — cited source does not exist: $src"
                })
            continue
        }
        if ($quoteLines.Count -eq 0) { continue }   # basis-only: source verified above, wording is authored
        $corpus = ConvertTo-Comparable -Text (Get-Content -LiteralPath $srcFull -Raw)
        if (-not (Test-Quotation -Quote ($quoteLines -join ' ') -Corpus $corpus)) {
            $excerpt = ConvertTo-Comparable -Text ($quoteLines -join ' ')
            if ($excerpt.Length -gt 80) { $excerpt = $excerpt.Substring(0, 80) + '...' }
            $script:Violations.Add([pscustomobject]@{
                    File = $rel; Line = $i + 1; Kind = 'yaml'
                    Quote = "$qid — $excerpt"
                })
        }
    }
}

# ---------------------------------------------------------------- report
if (-not $Quiet) {
    Write-Host "Checked $($script:Checked) quotation(s); skipped $($script:Skipped) marked [P]; $($script:Unmarked) unmarked."
}

if ($script:Violations.Count -eq 0) {
    Write-Host "OK - every source-derived quotation is verbatim." -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "NOT VERBATIM ($($script:Violations.Count))" -ForegroundColor Red
foreach ($v in $script:Violations) {
    Write-Host ("  {0}:{1}" -f $v.File, $v.Line) -ForegroundColor Yellow
    Write-Host ("      {0}" -f $v.Quote)
}
Write-Host ""
Write-Host "A quotation under [S] claims to be verbatim. Restore the source wording, or mark the" -ForegroundColor DarkGray
Write-Host "elision with '...' so the trim is visible. Do not relabel it [P] to silence this." -ForegroundColor DarkGray
exit 1
