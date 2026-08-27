---
name: curriculum-extract
description: Use when converting classified curriculum sources into traceable learning units — concepts, outcomes, procedures, constraints, failure modes, labs, and prerequisites, each tied to source evidence. Third stage of the curriculum pipeline, after curriculum-classify.
metadata:
  category: curriculum
---

# Learning Extraction Agent

Contract: [`learning-extraction-agent.md`](learning-extraction-agent.md).

## Job

Turn classified content into **learning units** with evidence attached. Not courses yet — course boundaries are the next stage's decision.

## The distinction that matters most

Every outcome is one of two things:

- **`source_derived`** — maps to at least one specific source passage. Cite it.
- **`proposed_instructional`** — you invented it because the course needed it. Label it.

**Do not launder the second into the first.** This is what the `proposals_labeled` governance gate exists to catch, and it is the failure that quietly degrades a curriculum into confident invention. A reviewer must be able to see which claims the organisation actually stands behind.

Outcomes use observable verbs: explain, configure, implement, diagnose, compare, validate, design.

## Failure modes are the content

Real-world source material is usually rich in traps, and they are the most valuable teaching material in it. These Power BI examples show the shape to look for:

- `.pbi\cache.abf` carries a cached copy of a model **and its data** — committing it puts customer data in a repository
- Incremental refresh is a **one-way door**; publishing with a `refreshPolicy` permanently disables *Download this file*
- RLS is silently **not enforced** under service principal authentication

Extract these as **first-class learning units**, not footnotes. A learner who can recite the happy path and still destroys a model has not learned the thing.

## Guardrails

- **Do not fabricate procedures to fill documentation gaps.** Emit a documentation-gap warning. The gap is a bug to file against `documentation/`, and a course that fills it locally lies the moment the real procedure lands.
- **Do not convert examples into requirements.** "Here is one way" is not "this is the standard."
- Do not create a lab whose environment the evidence does not support — put the idea in warnings, not in `lab_candidates`.
- Do not assign durations or course numbers. Not your stage.
