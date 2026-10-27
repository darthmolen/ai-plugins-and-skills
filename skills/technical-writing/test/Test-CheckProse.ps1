#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Self-test battery for check-prose.ps1.

.DESCRIPTION
    Each case writes one Markdown file to a temp directory, runs the checker over it, and asserts on
    the exit code and on which rule ids were reported.

    The warning fixtures are lines from learning/generated/ai-101/student-guide.md as generated at
    source revision 94116d6, which is the corpus the technical-writing skill was written against.
    The error fixtures are the clickbait forms that corpus does not contain; they are errors because
    nothing legitimate in technical prose looks like them.

    Every rule has a SILENT case beside it. A checker that reports a plain negation or a bolded term
    is one nobody leaves switched on.

.NOTES
    Exit 0 iff every case passes.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Checker = Join-Path $PSScriptRoot '..' 'scripts' 'check-prose.ps1'
$script:Pass = 0
$script:Fail = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()

function Invoke-Case {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Text,
        [Parameter(Mandatory)][int]    $ExpectExit,
        [string[]] $ExpectReported = @(),
        [string[]] $ExpectSilentOn = @(),
        [switch]   $Strict
    )

    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("pc-" + [guid]::NewGuid().ToString('N').Substring(0, 12))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'guide.md') -Value $Text -NoNewline

    # Write-Host writes to the information stream (6), not the success stream, so 6>&1 is
    # required or $out is empty and every content assertion silently passes on nothing.
    $out = & $script:Checker -Path $root -Strict:$Strict 6>&1 2>&1 | Out-String
    $code = $LASTEXITCODE
    $problems = [System.Collections.Generic.List[string]]::new()

    if ($code -ne $ExpectExit) { $problems.Add("exit $code, expected $ExpectExit") }
    foreach ($needle in $ExpectReported) {
        if ($out -notmatch [regex]::Escape($needle)) { $problems.Add("did not report '$needle'") }
    }
    foreach ($needle in $ExpectSilentOn) {
        if ($out -match [regex]::Escape($needle)) { $problems.Add("wrongly reported '$needle'") }
    }

    if ($problems.Count -eq 0) {
        $script:Pass++
        Write-Host "  PASS  $Name" -ForegroundColor Green
    }
    else {
        $script:Fail++
        $script:Failures.Add("$Name : $($problems -join '; ')")
        Write-Host "  FAIL  $Name — $($problems -join '; ')" -ForegroundColor Red
        Write-Host ($out -split "`n" | Select-Object -First 12 | Out-String) -ForegroundColor DarkGray
    }

    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "check-prose self-test" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------- clean prose
Invoke-Case -Name 'plain technical prose passes' -ExpectExit 0 -Strict `
    -ExpectSilentOn 'WARN  ', 'ERROR  ' -Text @'
# Context window limits

A **model** predicts the next token from the text in its context window. It does not remember
earlier calls. A session that seems to remember is re-sending the earlier conversation.

| Limit | Effect |
|-|-|
| No memory between calls | Earlier turns must be re-sent |
'@

# ---------------------------------------------------------------- errors: headings
Invoke-Case -Name 'heading ending in a question mark is an error' -ExpectExit 1 `
    -ExpectReported 'heading-question', 'guide.md:1' -Text @'
## Ever wondered why your build fails?

The restore step did not run.
'@

Invoke-Case -Name 'heading ending in an exclamation mark is an error' -ExpectExit 1 `
    -ExpectReported 'heading-question' -Text @'
## Grounding changes everything!
'@

Invoke-Case -Name 'all-caps heading is an error' -ExpectExit 1 `
    -ExpectReported 'heading-allcaps' -Text @'
## WHY YOU NEED TO DO THESE THINGS
'@

Invoke-Case -Name 'a question the section answers is a legitimate heading' -ExpectExit 0 `
    -ExpectSilentOn 'heading-question' -Text @'
### Why not an update script?

### How does the build find the bundles?
'@

Invoke-Case -Name 'a count-of-things clickbait heading is an error' -ExpectExit 1 `
    -ExpectReported 'filler-phrase' -Text @'
## Why you need to do these 3 things or your build fails
'@

Invoke-Case -Name 'heading with an acronym is not all-caps' -ExpectExit 0 `
    -ExpectSilentOn 'heading-allcaps' -Text @'
## MCP servers and the AI harness

### FAQ
'@

# ---------------------------------------------------------------- errors: prose
Invoke-Case -Name 'exclamation mark ending a sentence is an error' -ExpectExit 1 `
    -ExpectReported 'exclamation', 'guide.md:3' -Text @'
# Setup

Grounding fixes this!
'@

Invoke-Case -Name 'exclamation marks in code, images and operators are not reported' -ExpectExit 0 `
    -ExpectSilentOn 'exclamation', 'filler-phrase' -Text @'
# Setup

Run `echo done! && exit` and compare with `a != b`. ![diagram](d.png)
The string `but wait` is a literal argument here.

```bash
echo "done!"
```
'@

Invoke-Case -Name 'clickbait phrase is an error' -ExpectExit 1 `
    -ExpectReported 'filler-phrase' -Text @'
# Setup

But wait, there is more to configure.
'@

Invoke-Case -Name 'filler opener is an error' -ExpectExit 1 `
    -ExpectReported 'filler-phrase' -Text @'
# Setup

Let's dive in.
'@

Invoke-Case -Name 'a phrase inside a fenced block is not reported' -ExpectExit 0 `
    -ExpectSilentOn 'filler-phrase' -Text @'
# Setup

```text
Let's dive in.
```
'@

# ---------------------------------------------------------------- warnings: not X, it is Y
Invoke-Case -Name 'not-X-it-is-Y across two sentences warns (AI-101 L91)' -ExpectExit 0 `
    -ExpectReported 'not-x-it-is-y', 'WARN' -Text @'
> `[S]` The test is not whether you left the window. It is **who decided.**
'@

Invoke-Case -Name 'not-X-it-is-Y with a semicolon warns (AI-101 L174)' -ExpectExit 0 `
    -ExpectReported 'not-x-it-is-y' -Text @'
The rule is not *don't use the powerful tool*; it is **stay in the loop where the loop is
load-bearing.**
'@

Invoke-Case -Name 'trailing ", not a ..." contrast warns (AI-101 L46)' -ExpectExit 0 `
    -ExpectReported 'not-x-it-is-y' -Text @'
Choosing is an engineering decision with a cost attached, not a preference.
'@

Invoke-Case -Name 'not-X-it-is-Y with "we" warns (skill baseline L70)' -ExpectExit 0 `
    -ExpectReported 'not-x-it-is-y' -Text @'
We are not trying to make the model smarter. We are trying to make it correct about us.
'@

Invoke-Case -Name 'trailing contrast without an article warns (skill baseline L49)' -ExpectExit 0 `
    -ExpectReported 'not-x-it-is-y' -Text @'
The failures that cost real time are grounding failures, not capacity failures.
'@

Invoke-Case -Name 'a plain negation does not warn' -ExpectExit 0 `
    -ExpectSilentOn 'not-x-it-is-y' -Text @'
The model does not remember earlier calls. It is stateless, so the harness re-sends them.
'@

# ---------------------------------------------------------------- warnings: announcing importance
Invoke-Case -Name 'announcing importance warns (AI-101 L30, L150)' -ExpectExit 0 `
    -ExpectReported 'importance-claim' -Text @'
A model predicts the next token. That is the whole mechanism.

That last sentence is the entire distinction.
'@

# ---------------------------------------------------------------- warnings: intensifiers
Invoke-Case -Name 'intensifier warns (AI-101 L195)' -ExpectExit 0 `
    -ExpectReported 'intensifier' -Text @'
Its schemas ride along on every single request.
'@

# ---------------------------------------------------------------- warnings: bold sentences
Invoke-Case -Name 'a bolded sentence warns (AI-101 L56)' -ExpectExit 0 `
    -ExpectReported 'bold-sentence' -Text @'
The model gets the headlines. **The harness determines what you can actually accomplish.**
'@

Invoke-Case -Name 'a bolded term does not warn' -ExpectExit 0 `
    -ExpectSilentOn 'bold-sentence' -Text @'
A **harness** is the software around the model. **No memory between calls** is the first limit.
'@

Invoke-Case -Name 'a bold label opening a list item does not warn (code-quality L121)' -ExpectExit 0 `
    -ExpectSilentOn 'bold-sentence' -Text @'
2. **No production-codebase replication of the token finding.** Both CodeScene studies use
   competitive-programming corpora.
- **Effect sizes are modest at the file level.** Correlations are distributional.
'@

Invoke-Case -Name 'a bold citation header opening a line does not warn (code-quality L19)' -ExpectExit 0 `
    -ExpectSilentOn 'bold-sentence' -Text @'
**Wirdemann, Borg, Hagatulah, Tornhill (2026), "Code Health in LLM-Based Test Generation."**
The study measured token use against code health.
'@

Invoke-Case -Name 'a bold arrow label does not warn (limitations)' -ExpectExit 0 `
    -ExpectSilentOn 'bold-sentence' -Text @'
→ **Killed at Stage 5 — Implementation and test review by a person**
'@

Invoke-Case -Name 'a bold table cell does not warn' -ExpectExit 0 `
    -ExpectSilentOn 'bold-sentence' -Text @'
| Mechanism | Evidence |
|-|-|
| Filter | **Strongest of the four axes in the published literature** |
'@

Invoke-Case -Name 'bold with nested italic does not pair across table rows' -ExpectExit 0 `
    -ExpectSilentOn 'bold-sentence' -Text @'
| Question | Before | After |
|-|-|-|
| "What kind of skill is this?" | Unanswerable from category | **The category *is* the archetype** |
| "Do families sort together?" | Only by accident | **Yes, by prefix** |
'@

# ---------------------------------------------------------------- warnings: headings
Invoke-Case -Name 'a count with no subject as a heading warns (AI-101 L34)' -ExpectExit 0 `
    -ExpectReported 'heading-count' -Text @'
### The three consequences
'@

Invoke-Case -Name 'a count that names its subject does not warn' -ExpectExit 0 `
    -ExpectSilentOn 'heading-count' -Text @'
### Three limits of next-token prediction
'@

Invoke-Case -Name 'a Summary or Key takeaways heading warns' -ExpectExit 0 `
    -ExpectReported 'heading-recap' -Text @'
## Key takeaways
'@

Invoke-Case -Name 'a singular Key takeaway heading warns (skill baseline L68)' -ExpectExit 0 `
    -ExpectReported 'heading-recap' -Text @'
### Key takeaway
'@

Invoke-Case -Name 'an intensifier in a heading warns (skill baseline L38)' -ExpectExit 0 `
    -ExpectReported 'intensifier' -Text @'
### 4. Grounding is what we actually want
'@

# ---------------------------------------------------------------- severity
Invoke-Case -Name '-Strict turns a warning into a failure' -ExpectExit 1 -Strict `
    -ExpectReported 'intensifier' -Text @'
Its schemas ride along on every single request.
'@

Write-Host ""
if ($script:Fail -eq 0) {
    Write-Host "OK — $($script:Pass) case(s) passed." -ForegroundColor Green
    exit 0
}
Write-Host "$($script:Fail) of $($script:Pass + $script:Fail) case(s) FAILED:" -ForegroundColor Red
foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
exit 1
