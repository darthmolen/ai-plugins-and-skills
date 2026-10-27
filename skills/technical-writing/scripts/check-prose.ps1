#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Reports the prose patterns the technical-writing skill forbids, in Markdown files.

.DESCRIPTION
    The mechanical half of the technical-writing skill. It finds the patterns a regex can find and
    leaves the rest to a reader: whether a paragraph repeats an earlier one, and whether a heading
    says what its section is about, are judgement calls this script does not make.

    Two severities.

      ERROR  nothing legitimate in technical prose looks like this
             heading-question   heading ending in ? or !
             heading-allcaps    heading with no lower-case letters
             exclamation        a sentence ending in !
             filler-phrase      clickbait and filler ("but wait", "let's dive in", "here's why")

      WARN   usually a defect, occasionally the right sentence; a reader decides
             not-x-it-is-y      "The test is not whether you left. It is who decided."
             importance-claim   telling the reader a point matters instead of making it
             intensifier        "actually", "every single", "dramatically"
             bold-sentence      six or more words in bold
             heading-count      a count with no subject as a heading ("The three consequences")
             heading-recap      Summary / Key takeaways / Recap

    The warning rules were calibrated against learning/generated/ai-101/student-guide.md at source
    revision 94116d6, where they found the patterns that made that guide read as written for a
    feed rather than for a reader. See test/Test-CheckProse.ps1 beside this script for the fixtures.

    Fenced code blocks and inline code are skipped. Tables, block quotes and list items are prose
    and are checked.

.PARAMETER Path
    A Markdown file or a directory to scan recursively. Defaults to learning/generated under the
    current directory, which is where a curriculum workspace keeps its rendered artifacts.

.PARAMETER Strict
    Warnings fail the run as well as errors.

.PARAMETER Quiet
    Print errors and the summary only.

.EXAMPLE
    & $checkProse                              # learning/generated
    & $checkProse -Path documentation -Strict
#>
[CmdletBinding()]
param(
    [string] $Path,
    [switch] $Strict,
    [switch] $Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Path) { $Path = Join-Path (Get-Location) 'learning' 'generated' }
$Path = (Resolve-Path -LiteralPath $Path).Path

$script:Findings = [System.Collections.Generic.List[pscustomobject]]::new()

$IgnoreCase = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase

$FillerPhrases = [regex]::new(
    "\b(?:let'?s (?:dive|dig|jump) in|but wait|there'?s more|here'?s (?:the thing|why|the kicker|the catch)|" +
    "buckle up|game[- ]changer|in conclusion|without further ado|you might be (?:thinking|wondering)|" +
    "ever wondered|the secret (?:to|is)|you won'?t believe|happy coding|why you need to|" +
    "(?:\d+|three|four|five|seven|ten) (?:things|tricks|secrets|mistakes) (?:you|that|every)|" +
    "or you(?:'ll| will)? (?:die|regret it|be sorry))\b", $IgnoreCase)

# A question heading is legitimate when it is the question its section answers ("Why not an update
# script?" in an ADR). A teaser asks something the reader did not ask ("Ever wondered...?").
$AnsweredQuestion = [regex]::new(
    '^(?:why|how|what|when|where|which|who|whose|should|can|could|does|do|is|are|was|will|must)\b', $IgnoreCase)

$ImportanceClaims = [regex]::new(
    "\b(?:(?:that|this) is (?:the whole|the entire|exactly) (?:mechanism|point|distinction|reason|why)|" +
    "the (?:whole|entire) (?:mechanism|point|distinction)|if you take away nothing else|matters more than|" +
    "the key (?:insight|takeaway)|the most important thing|make no mistake|" +
    "it(?:'s| is) (?:important|worth) (?:to note|noting))\b", $IgnoreCase)

$Intensifiers = [regex]::new(
    "\b(?:actually|every single|dramatically|truly|simply|incredibly|extremely|absolutely|literally|" +
    "crucial(?:ly)?|seamless(?:ly)?|powerful)\b", $IgnoreCase)

$NotXItIsY = @(
    # The test is not whether you left the window. It is who decided.
    [regex]::new("\b(?:is|are|was|were|isn't|aren't)\s+(?:not\s+|never\s+)?(?<=n't\s|not\s|never\s)[^.;:!?]{1,120}?[.;:—–]\s*(?:it|they|this|that|we|you)(?:\s+(?:is|are|was|were)|'s|'re)\b", $IgnoreCase),
    # ... an engineering decision with a cost attached, not a preference.
    [regex]::new(",\s+not\s+(?:(?:a|an|the)\s+)?[^,.;]{1,60}\.", $IgnoreCase),
    # not X, but Y
    [regex]::new("\bnot\s+[^,.;]{1,60},\s+but\b", $IgnoreCase)
)

# Single asterisks are allowed inside, for nested italic. Without that, "**The category *is* the
# archetype**" ends at the italic and its closing ** pairs with the next table row's opening **.
$BoldSpan = [regex]::new('\*\*((?:[^*]|\*(?!\*))+?)\*\*')
$LabelPrefix = [regex]::new('^\s*(?:>\s*)*(?:[-*+]\s+|\d+\.\s+|(?:→|->)\s*)?$')
$CountHeading =[regex]::new('^(?:the\s+)?(?:two|three|four|five|six|seven|eight|nine|ten|\d+)(?:-part)?\s+[\w-]+$', $IgnoreCase)
$RecapHeading = [regex]::new('^(?:summary|in summary|key takeaways?|takeaways?|recap|tl;?dr|conclusion)$', $IgnoreCase)

function Add-Finding {
    param([string] $Severity, [string] $File, [int] $Line, [string] $Rule, [string] $Text)
    $snippet = ($Text -replace '\s+', ' ').Trim()
    if ($snippet.Length -gt 90) { $snippet = $snippet.Substring(0, 87) + '...' }
    $script:Findings.Add([pscustomobject]@{
        Severity = $Severity; File = $File; Line = $Line; Rule = $Rule; Text = $snippet
    })
}

function ConvertTo-Prose {
    <# Blank out what is not prose, keeping character positions so line numbers still map. #>
    param([string] $Text)
    $Text = [regex]::Replace($Text, '`[^`]*`', { param($m) ' ' * $m.Length })
    $Text = [regex]::Replace($Text, '!\[', '  ')
    $Text = [regex]::Replace($Text, '\]\([^)]*\)', { param($m) ']' + (' ' * ($m.Length - 1)) })
    return $Text
}

function Test-Heading {
    param([string] $File, [int] $Line, [string] $Heading)

    $h = ($Heading -replace '\s+#+\s*$', '').Trim()
    $prose = ConvertTo-Prose $h

    if ($prose -match '!\s*$' -or ($prose -match '\?\s*$' -and -not $AnsweredQuestion.IsMatch($prose))) {
        Add-Finding 'ERROR' $File $Line 'heading-question' $h
    }
    if ($prose -cnotmatch '[a-z]' -and [regex]::Matches($prose, '\b[A-Z]{2,}\b').Count -ge 3) {
        Add-Finding 'ERROR' $File $Line 'heading-allcaps' $h
    }
    foreach ($m in $FillerPhrases.Matches($prose)) { Add-Finding 'ERROR' $File $Line 'filler-phrase' $h }
    foreach ($m in $Intensifiers.Matches($prose)) { Add-Finding 'WARN' $File $Line 'intensifier' $h }

    # "Module 2 — Leak versus investment": judge the part after the module label.
    $subject = ($prose -replace '^Module\s+\d+\s*[—–:-]\s*', '').Trim()
    if ($CountHeading.IsMatch($subject)) { Add-Finding 'WARN' $File $Line 'heading-count' $h }
    if ($RecapHeading.IsMatch($subject)) { Add-Finding 'WARN' $File $Line 'heading-recap' $h }
}

function Test-Paragraph {
    param([string] $File, [int] $FirstLine, [string] $Text)

    $prose = ConvertTo-Prose $Text
    $flat = $prose -replace "`n", ' '

    $lineOf = {
        param([int] $Index)
        $FirstLine + ([regex]::Matches($prose.Substring(0, $Index), "`n")).Count
    }

    foreach ($m in [regex]::Matches($flat, "[\w)\]*_""']!(?=\s|$)")) {
        Add-Finding 'ERROR' $File (& $lineOf $m.Index) 'exclamation' $Text.Split("`n")[(& $lineOf $m.Index) - $FirstLine]
    }
    foreach ($m in $FillerPhrases.Matches($flat)) {
        Add-Finding 'ERROR' $File (& $lineOf $m.Index) 'filler-phrase' $m.Value
    }
    foreach ($rx in $NotXItIsY) {
        foreach ($m in $rx.Matches($flat)) { Add-Finding 'WARN' $File (& $lineOf $m.Index) 'not-x-it-is-y' $m.Value }
    }
    foreach ($m in $ImportanceClaims.Matches($flat)) {
        Add-Finding 'WARN' $File (& $lineOf $m.Index) 'importance-claim' $m.Value
    }
    foreach ($m in $Intensifiers.Matches($flat)) {
        Add-Finding 'WARN' $File (& $lineOf $m.Index) 'intensifier' $m.Value
    }
    foreach ($m in $BoldSpan.Matches($prose)) {
        # Bold that opens a line, list item, arrow line or table cell is a label a reader scans
        # for: a citation header, a list lead, a stage name. Only bold inside a sentence warns.
        $start = [Math]::Max($prose.LastIndexOf("`n", [Math]::Max($m.Index - 1, 0)), $prose.LastIndexOf('|', [Math]::Max($m.Index - 1, 0))) + 1
        if ($m.Index -eq 0) { $start = 0 }
        if ($LabelPrefix.IsMatch($prose.Substring($start, $m.Index - $start))) { continue }
        $words = @($m.Groups[1].Value -split '\s+' | Where-Object { $_ }).Count
        if ($words -ge 6) { Add-Finding 'WARN' $File (& $lineOf $m.Index) 'bold-sentence' $m.Value }
    }
}

function Test-File {
    param([string] $FullName, [string] $Display)

    $lines = [System.IO.File]::ReadAllLines($FullName)
    $inFence = $false
    $para = [System.Collections.Generic.List[string]]::new()
    $paraStart = 0

    $flush = {
        if ($para.Count -gt 0) { Test-Paragraph $Display $paraStart ($para -join "`n") }
        $para.Clear()
    }

    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        $n = $i + 1

        if ($line -match '^\s*(```|~~~)') { & $flush; $inFence = -not $inFence; continue }
        if ($inFence) { continue }

        if ($line -match '^\s{0,3}#{1,6}\s+(.+)$') { & $flush; Test-Heading $Display $n $Matches[1]; continue }
        if ($line -match '^\s*$') { & $flush; continue }

        if ($para.Count -eq 0) { $paraStart = $n }
        $para.Add($line)
    }
    & $flush
}

$files = @(if (Test-Path -LiteralPath $Path -PathType Leaf) {
        Get-Item -LiteralPath $Path
    }
    else {
        Get-ChildItem -LiteralPath $Path -Recurse -File -Filter '*.md' | Sort-Object FullName
    })
$base = if (Test-Path -LiteralPath $Path -PathType Leaf) { Split-Path -Parent $Path } else { $Path }

foreach ($f in $files) {
    $display = [System.IO.Path]::GetRelativePath($base, $f.FullName) -replace '\\', '/'
    Test-File $f.FullName $display
}

$errors = @($script:Findings | Where-Object Severity -eq 'ERROR')
$warnings = @($script:Findings | Where-Object Severity -eq 'WARN')

foreach ($x in $script:Findings) {
    if ($Quiet -and $x.Severity -eq 'WARN') { continue }
    $colour = if ($x.Severity -eq 'ERROR') { 'Red' } else { 'Yellow' }
    Write-Host ("  {0,-5}  {1}:{2}  [{3}]  {4}" -f $x.Severity, $x.File, $x.Line, $x.Rule, $x.Text) -ForegroundColor $colour
}

Write-Host ""
Write-Host ("check-prose: {0} file(s), {1} error(s), {2} warning(s)." -f $files.Count, $errors.Count, $warnings.Count)

if ($errors.Count -gt 0 -or ($Strict -and $warnings.Count -gt 0)) {
    Write-Host "See the technical-writing skill for each rule and its fix." -ForegroundColor DarkGray
    exit 1
}
exit 0
