---
name: curriculum-generate
description: Use when rendering approved course definitions into artifacts — student guide, instructor guide, labs, and quiz — with provenance metadata. Fifth stage of the curriculum pipeline, after curriculum-govern approves the definition. Slide generation is not implemented.
metadata:
  category: curriculum
---

# Artifact Generation Agent

Contract: [`artifact-generation-agent.md`](artifact-generation-agent.md).

## Job

Render `learning/courses/<id>/course.yaml` plus its sources into `learning/generated/<id>/`:

```text
manifest.json · student-guide.md · instructor-guide.md · labs.md · quiz.yaml
```

`.pptx` slides are **not implemented**. Say so rather than emitting a Markdown file named like a deck.

## Everything you write here is disposable

Output goes to `learning/generated/`, which is regenerable by definition and which nobody may hand-edit.

**Never write into `courses/<id>/content/`.** That is authored material; overwriting it destroys work that cannot be rebuilt. This is invariant #3 and the reason the two trees are separate rather than interleaved per course.

## Provenance is mandatory

Every artifact carries content hash, generator version, and source revision in `manifest.json`. An artifact without one did not come from this pipeline and must not be published. It is also what makes reprocessing unchanged content idempotent, and what lets the M365 sync agent skip what has not changed instead of re-uploading everything.

## Writing for the audience

Assume much of this curriculum is read by analysts using developer tooling under duress. One tone rule dominates: **organize by the task the reader is trying to do**, show the prompt to type rather than the API to call, and put anything that can destroy work or leak data in a checklist *before* the instructions — not in an explanation after them.

Signpost where depth begins so a reader knows when they can stop. Assume no git. Link jargon to a glossary rather than redefining it or assuming it.

## Guardrails

- Render only what the course definition and its sources support. A gap in the source is a gap in the artifact and a warning — not a place to improvise.
- Keep `source_derived` and `proposed_instructional` distinguishable in the output. The instructor needs to know which is which before standing up in front of a room.
- Re-run governance against what you produced. An approved definition can still render an unapproved artifact.
