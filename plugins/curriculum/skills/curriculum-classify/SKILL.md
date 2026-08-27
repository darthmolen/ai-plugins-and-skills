---
name: curriculum-classify
description: Use when sorting a curriculum source manifest into content domains and teachability — deciding which discovered files are eligible instructional source and what kind of content each holds. Second stage of the curriculum pipeline, after curriculum-discover.
metadata:
  category: curriculum
---

# Content Classification Agent

Contract: [`content-classification-agent.md`](content-classification-agent.md).

## Job

Take the source manifest and label each item: content domain, document type, lifecycle state, and whether it is usable as instructional source.

## Lifecycle state is the part people skip

A source that is **deprecated, superseded, or draft** must be labeled as such, not silently treated as current. Two conventions worth hunting for in any source repo make this concrete, and both will bite:

- A `planning/not-implemented/` directory usually holds work that was **superseded before it was carried out** and is retained deliberately, so the reasoning survives. Teaching from it would teach a ranking its own repository says is wrong.
- A repo that tags claims `[VERIFIED]`, `[REASONED]`, and `[TEST THIS]` is telling you the confidence level. A `[REASONED]` claim is analysis, not fact. A course that presents one as settled is a governance failure.

Carry those tags forward. Do not launder them.

## Guardrails

- Do not extract learning content here — that is the next stage. Classify only.
- Do not mark something eligible because it would be convenient. A thin or contradictory source classified as usable produces a confident, wrong course.
- Contradictions between sources get **recorded**, not resolved. Resolution is a governance decision with a human in it.
