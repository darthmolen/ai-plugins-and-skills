---
name: curriculum-design
description: Use when turning extracted learning units into course definitions — deciding course boundaries, level, prerequisites, modules, labs, and assessments, and writing course.yaml. Fourth stage of the curriculum pipeline, after curriculum-extract.
metadata:
  category: curriculum
---

# Curriculum Design Agent

Contract: [`curriculum-design-agent.md`](curriculum-design-agent.md). Schema: [`schema.yaml`](schema.yaml).

## Job

Group learning units into courses, set the prerequisite graph, and emit `learning/courses/<id>/course.yaml`.

## Check `status` before you write anything

**A course definition is editorial, not derived.** Which facts to teach, in what order, to whom, for
how long, and with what emphasis are not in the source documents — they cannot be regenerated,
because they were never there. So a definition is scaffolded once and owned thereafter.

| Course status | What you may do |
|---|---|
| absent | Author it at `status: starter` |
| `starter` | Regenerate wholesale |
| `draft` and above | **Propose a diff. Do not write the file.** |

Above `starter`, print the proposed change set and stop. Overwriting a reconciled definition
destroys judgement that no source can give back.

This is also the answer when generation comes out wrong. Fix it at the layer that owns it:

| Wrong thing | Fix here |
|---|---|
| A fact | `documentation/` |
| Emphasis, selection, audience, length, order | `course.yaml` |
| Structure or tone of the rendered output | the generator skill |
| | **never** `learning/generated/` |

## Emphasis lives in `note`

Every `source_locations` entry takes a `note`, and the generator reads it. It is how two courses
citing the same document teach different things from it — *"the spine: two candidates, leak vs
investment"* versus *"the worked example of a leak."* A missing `note` is a course that will render
as a restatement of its sources.

## Where the course lives

**With the source repo it teaches**, not in the curriculum tooling repo by default. A course about reporting belongs with the reporting repo; a course about git and general practice belongs with the practice repo. Record the mapping in your `sources.yaml` under `course_locations`.

## Design the prerequisite honestly

The most consequential mistake in one starter set was a prerequisite that excluded the course's own audience: a report-authoring course required "Basic Git knowledge" while existing to train report authors who — by their own repo's description — put their work where they did *precisely to avoid a development process*.

**A prerequisite that gates out the population the course was created for is a design defect, not a filter.** When the audience and the material are mismatched, split the course by audience rather than raising the bar. That is why the analyst and engineer halves ended up as separate courses, with a git primer in front of the analyst half only.

## Levels

101 foundation · 201 guided application · 301 independent implementation · 401 advanced architecture · 501+ capstone and specialization.

## Guardrails

- Every course needs at least one `source_locations` entry **outside `learning/`**. A course sourced only from `learning/` has become its own source, and the `sources_are_upstream` gate fails it.
- Repo-qualify every source as `{repo, path}`. Point at files that exist **today**, not where you intend to write something — that is exactly how the original source map drifted.
- Durations are proposals until a pilot group says otherwise. Label them as such.
- **Never overwrite a definition at `draft` or above.** Propose; do not write.
- Promoting a course to `draft` means pinning every source with `revision:`. The status is a claim
  that it was reconciled; the pin is the evidence. `sources_reconciled` fails an unpinned `draft`.
- Run `.\scripts\validate-curriculum.ps1` before handing off. Its failure messages print the exact
  pin to paste — you do not have to compute a digest by hand.
