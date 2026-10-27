---
name: curriculum-generate
description: Use when rendering approved course definitions into artifacts — student guide, instructor guide, slide outline, labs, and quiz — with provenance metadata. Fifth stage of the curriculum pipeline, after curriculum-govern approves the definition. Markdown is the version of record; final Office files are rendered in M365, and in-repo .docx/.pptx are optional.
metadata:
  category: curriculum
---

# Artifact Generation Agent

Contract: [`artifact-generation-agent.md`](artifact-generation-agent.md).

## Job

Render `learning/courses/<id>/course.yaml` plus its sources into `learning/generated/<id>/`:

```text
manifest.json · student-guide.md · instructor-guide.md · slide-outline.md · labs.md · quiz.yaml
```

**That Markdown is the version of record, and it is the whole mandatory job.** Markdown diffs, reviews and
hashes; Office files do not. The final `.pptx` and `.docx` are rendered downstream from it, by whatever your
publish step uses (an Office Copilot agent, pandoc, a deck generator).

**In-repo Office files are optional.** Render a `.docx` (pandoc) or `.pptx` (a course's deck generator) only when
asked. If you render one, or a course already has one, it follows the contract's binary rules: `ooxml-stable` hash,
recorded renderer invocation, rendered twice to prove determinism, and **never left lagging its Markdown**. A stale
binary is a defect; say so in `known_defects`.

**A script you write during this run goes in `learning/generated/<id>/` and the manifest.** A deck
generator written by hand for one course does not: the pipeline cannot rebuild it, so it lives in
`learning/scripts/<id>/`, outside the manifest, listed in `learning/scripts/README.md`.

## Everything you write here is disposable

Output goes to `learning/generated/`, which is regenerable by definition and which nobody may hand-edit.

**Never write into `courses/<id>/content/`.** That is authored material; overwriting it destroys work that cannot be rebuilt. This is invariant #3 and the reason the two trees are separate rather than interleaved per course.

## Provenance is mandatory

Every artifact carries content hash, generator version, and source revision in `manifest.json`. An artifact without one did not come from this pipeline and must not be published. It is also what makes reprocessing unchanged content idempotent, and what lets a publish job skip what has not changed instead of re-uploading everything.

## Writing for the audience

**REQUIRED SUB-SKILL:** load `technical-writing` (from the main `ai-plugins-and-skills` plugin) before writing the first artifact. Its rules govern every sentence you generate, and they apply to what you take from a source as much as to what you write yourself. A source that uses teaser headings or "not X, it is Y" lines does not license you to copy them.

Before handing anything off, run its checker over what you produced and fix every error. Review each warning and either fix it or keep the sentence as written:

```powershell
# check-prose.ps1 ships in the technical-writing skill's scripts/ folder
& "<technical-writing skill dir>\scripts\check-prose.ps1" -Path learning/generated/<slug>
```

Then do the two reads the skill describes: headings only, then each paragraph against the ones before it.

Most of this curriculum is read by analysts using developer tooling under duress. One tone rule dominates: **organize by the task the reader is trying to do**, show the prompt to type rather than the API to call, and put anything that can destroy work or leak data in a checklist *before* the instructions — not in an explanation after them.

Signpost where depth begins so a reader knows when they can stop. Assume no git. Link jargon to a glossary rather than redefining it or assuming it.

## Every quotation must be verbatim, and there is a script for it

Run it before you hand anything off. It is not optional and it is not a formatting nicety:

```powershell
# from the workspace root; check-quotations.ps1 ships beside the curriculum-govern skill
& "<curriculum-govern skill dir>\check-quotations.ps1"            # exit 0 required
```

**Eight quotations shipped into a production curriculum's courses having been trimmed, elided or
re-punctuated while still presented as exact.** None changed a meaning. Every one would have
survived a read-through, which is precisely why reading is not the control.

Two conventions make the check possible, and both are your responsibility while writing:

| Writing | Means | Checked |
|---|---|---|
| `` `[S]` `` before a `*"quotation"*` | you are claiming it is verbatim from a cited source | **yes** |
| `` `[P]` `` before a `*"quotation"*` | invented — instructor dialogue, an anticipated question | no |
| `quote:` in a YAML `evidence:` block | verbatim from the file `source:` names | **yes** |
| `basis:` in a YAML `evidence:` block | a synthesis you authored *from* that source | source path only |

**An elision is legal when you mark it.** `...` splits a quotation and each fragment is checked
separately, so trimming the middle of a long passage is fine — silently trimming it is not.

**Do not relabel a failing quotation `[P]` or `basis:` to make the check pass.** That is the
laundering this exists to prevent. Relabel only when the content genuinely is authored, and say so
in the manifest when you do.

## Guardrails

- Render only what the course definition and its sources support. A gap in the source is a gap in the artifact and a warning — not a place to improvise.
- Keep `source_derived` and `proposed_instructional` distinguishable in the output. The instructor needs to know which is which before standing up in front of a room.
- Re-run governance against what you produced. An approved definition can still render an unapproved artifact.
