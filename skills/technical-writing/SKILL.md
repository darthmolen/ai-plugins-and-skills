---
name: technical-writing
description: 'Use when writing or editing prose someone else will read — documentation, READMEs, ADRs, plans, skill files, PR descriptions, curriculum artifacts — and when generated text reads like marketing copy, a feed post, or a sermon: teaser headings, "not X, it is Y" lines, bolded slogans, sections that restate the one above.'
metadata:
  category: language-helpers
---

# Technical Writing

The reader came for information. Each sentence gives them some or costs them time. These rules
remove the sentences that cost time without giving anything.

They apply to every document in a repository, and they apply hardest to generated text. A model
reproduces the rhetoric of whatever it was trained on, and much of that was written to hold
attention rather than to inform.

## The rules

1. Headings say what the section contains. A reader skimming only the headings should be able
   to find the section that answers their question. A heading that teases, quotes a slogan, or
   counts things without naming them fails that test.
2. No clickbait. No teaser questions, no exclamation marks, no capitals for volume, no
   `but wait`, `here's why`, `you won't believe`, `N things you must do`.
3. Make the point; do not announce it. Delete sentences that tell the reader a point is
   important, central, or the whole idea. If it matters, the reader will see why from the point
   itself.
4. Say it once. Delete the paragraph, pull quote, closing line or summary that restates what
   the reader just read. Before deleting, check whether the restatement carries a fact the first
   statement lacks. If it does, move that fact into the first statement, then delete. A recap
   belongs only at the end of a long reference document, as a table the reader will come back to.
5. State what is true. "It is not X, it is Y" spends a sentence on a claim nobody made. Write Y.
   Keep "not X" only when a reader would assume X if you did not rule it out: a scope limit
   ("single-shot, not agentic"), a misconception the document exists to correct, or a default the
   reader brings with them. To test it, delete "not X" and ask whether a reader could now wrongly
   assume X. If they could, keep it. If X is a position set up only to be knocked down, delete it.
   The two-sentence form `It is not X. It is Y.` is never needed. When X earns its place, put it
   in the same sentence as Y.
6. No intensifiers. `actually`, `every single`, `dramatically`, `truly`, `simply`, `crucial`
   and `powerful` add emphasis and no information.
7. Bold marks terms and labels. Use it for a term at the point it is defined, or for a label
   that opens a list item, a line or a table cell: a citation, a stage name, a list lead. Keep
   labels short. Do not bold a sentence inside a paragraph.
8. Be specific. Give the command, the number, the file name, the error text. "Various", "a
   number of" and "in some cases" tell the reader that you know something you have not said.
9. No metaphor in place of a claim. "It starts every session as a stranger" is memorable and
   tells the reader nothing they can act on. Say what it lacks and what that causes. A term the
   document defines and then uses ("transducer", "load-bearing") is not a metaphor; keep it.
10. One hedge at most. If you are unsure, say so once, where it applies.

When you cannot tell whether a rule applies, leave the text as it is. A wrong edit costs more than
a missed one. This covers quotations, both verbatim from a source and the document's own
illustrative quotes and personas, as well as defined terms and document titles.

## Before and after

Taken from a generated student guide. Each "before" is a real line.

Teaser headings (rule 1):

```text
before: ### The three consequences
after:  ### Limits that follow from next-token prediction

before: ## Module 4 — What we fight: context
after:  ## Module 4 — Context window limits

before: ### Why we don't let it off the leash
after:  ### Why a development harness needs supervision
```

Announcing importance (rule 3):

```text
before: A model is a function that predicts the next token. That is the whole mechanism.
after:  A model is a function that predicts the next token.

before: That last sentence is the entire distinction, and it is why the answer to "which
        should I use" is not "the more capable one."
after:  (delete — the table above it already says this)
```

Not X, it is Y (rule 5), with a bolded slogan (rule 7):

```text
before: The test is not whether you left the window. It is **who decided.**
after:  What counts is who decided you should leave the window: you, or the tool.

before: The rule is not *don't use the powerful tool*; it is **stay in the loop where the
        loop is load-bearing.**
after:  Use the development harness, and review its work at the points where a mistake
        would ship.

keep:   The models were evaluated single-shot, not agentic.
        (a reader would otherwise assume the agentic setting the rest of the page discusses)
```

Restating (rule 4):

```text
before: [three paragraphs on capacity versus attention]
        None are capacity failures. All are grounding failures, and every one gets worse
        if you respond by giving the model more room to reason unaided.
after:  [the three paragraphs, ending where the argument ends]
```

## Writing from a source that breaks these rules

Do not carry the source's rhetoric into what you write. Restate its claims plainly. A verbatim
quotation has to stay verbatim, so when the source's wording breaks a rule, quote less of it and
state the claim in your own words.

If the source is in the same repository, raise its violations rather than reproducing them. The
generated text gets fixed the next time it is regenerated; the source does not.

## Checking

The skill ships `scripts/check-prose.ps1`, which finds what a regex can find. It needs PowerShell 7.
Where there is no shell, as in claude.ai or ChatGPT, apply the rules in the table below yourself,
then do the two reads. The script speeds the check up; the rules stand without it.

```powershell
$tw = if ($env:CLAUDE_PLUGIN_ROOT) {
  "$env:CLAUDE_PLUGIN_ROOT\skills\technical-writing\scripts"
} else {
  "$HOME\.claude\skills\technical-writing\scripts"
}
& "$tw\check-prose.ps1"                            # learning/generated under the current directory
& "$tw\check-prose.ps1" -Path documentation        # any file or directory
& "$tw\check-prose.ps1" -Path README.md -Strict    # warnings fail too
```

| Severity | Rule id | Rule |
|---|---|---|
| error | `heading-question` | heading ending in `!`, or a `?` heading that is a teaser; "Why not an update script?" is allowed |
| error | `heading-allcaps` | heading with no lower case |
| error | `exclamation` | sentence ending in `!` |
| error | `filler-phrase` | clickbait and filler phrases |
| warn | `not-x-it-is-y` | rule 5 |
| warn | `importance-claim` | rule 3 |
| warn | `intensifier` | rule 6 |
| warn | `bold-sentence` | six or more words in bold inside a sentence; a label opening a line, list item or table cell is exempt |
| warn | `heading-count` | a count with no subject, such as "The five questions" |
| warn | `heading-recap` | Summary, Key takeaways, Recap |

Errors always fail. Each warning needs a decision: fix it, or keep it because the sentence is right
as written. The test battery is `test/Test-CheckProse.ps1`.

The script cannot check rules 1, 4, 8 and 9. After it passes, do two reads:

1. Headings only. Read the headings in order without the body. Rename any heading that does not
   tell you what its section contains.
2. Paragraph by paragraph. For each paragraph, ask whether an earlier one already said it. If
   it did, delete the later one. Do not merge the two.

## Rationalizations

| Thought | Answer |
|---|---|
| "The bold helps skimmers." | Skimmers scan for terms. A bolded sentence is something they have to read. |
| "The summary helps retention." | Readers retain what they act on. A recap only makes them read the same thing twice. |
| "The contrast makes it memorable." | It makes the wrong claim memorable too. |
| "The source wrote it that way." | Then the source needs fixing as well. Do not copy it. |
| `It is a warning, not an error.` | A warning is a decision you owe. Leaving it unexamined is not one of the options. |
