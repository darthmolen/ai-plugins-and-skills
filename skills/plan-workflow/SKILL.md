---
name: plan-workflow
description: Manages planning documentation through the kanban board at planning/. Use when creating, progressing, or completing plans to ensure every feature document lands in the right directory (planning/, in-progress/, completed/, or backlog/) and stays in source control alongside the code it describes. Also covers parallel tracks — one in-progress plan per track when independent plans run in sub-agents.
metadata:
  category: workflow-composers
---

# Plan Workflow

This skill is the base layer of all spec-driven work. It governs how planning documents move through the kanban directories at `planning/` — whether you are working solo (simple spec-driven) or feeding into the adversarial review pipeline (complex spec-driven). Every layer of the workflow builds on top of this one; the kanban and commit discipline here apply regardless of which path you are on.

The two layers:

- **Simple spec-driven** — use this skill alone to author + execute the plan; no *plan-review* second session. The code-review caboose still applies to anything that ships (the review floor) — see the `spec-tier` skill.
- **Complex spec-driven (adversarial)** — use this skill plus `plan-send-review`, `plan-intake-review`, and `plan-receive-review`. The review trio inserts a gate into the same kanban flow before execution starts; the directories and commit discipline here still apply throughout.

## Directory Map

```
planning/
├── (root)          # Newly written plans waiting to start, or plans returned from in-progress
├── in-progress/    # Plans currently executing — one per track (see Parallel Tracks)
├── completed/      # Finished plans — final status recorded, moved here after execution
└── backlog/        # Items discovered during planning that require their own future planning session
```

The directories are committed to source control. Every plan document travels the path below; no document is ever deleted — only moved.

---

## Decision Tree: Where Does This Plan Go?

```
Did plan mode just produce a new plan?
│
├─ YES — Was a document already written to planning/?
│         │
│         ├─ YES → Leave it in planning/. It is queued and ready.
│         │
│         └─ NO  → Write it directly to planning/in-progress/.
│                  (Exiting plan mode into immediate execution skips the queue.)
│
└─ NO — Is this an update to an existing plan?
          │
          ├─ Execution finished successfully →
          │    1. Append a ## Status section to the document (see template below).
          │    2. Move the document from planning/in-progress/ to planning/completed/.
          │
          ├─ Execution interrupted / not completed →
          │    1. Append a ## Status section noting where work stopped.
          │    2. Move the document back to planning/ (the queue root).
          │
          └─ Item discovered during planning, outside current scope →
               Write a new document to planning/backlog/ (see Backlog section).
```

---

## File Naming

Use the format: `feature_<name>_<YYYY-MM-DD>.md`

Examples:
- `planning/feature_auth-refactor_2026-06-22.md`
- `planning/in-progress/feature_auth-refactor_2026-06-22.md`
- `planning/completed/feature_auth-refactor_2026-06-22.md`
- `planning/backlog/feature_session-token-storage_2026-06-22.md`

Keep the same filename as the document moves through directories — the path tells you the status.

---

## Plan Document Structure

Every plan document starts with this header:

```markdown
# [Feature / Bug / Task Name]

**Status:** Planned | In Progress | Completed | Blocked
**Track:** main | <track-slug>
**Date:** YYYY-MM-DD
**Author:** [name or agent]

## Objective
One sentence: what this plan achieves when done.

## Success Criteria
- [ ] Criterion 1
- [ ] Criterion 2

## Approach
How the work will be done. Include files to be modified and any dependencies.

## Phases
### Phase 1 — [Name]
[What to do. Mark async-eligible steps with [ASYNC].]

### Phase 2 — [Name]
...

## Dependencies / Prerequisites
List blocking work or required context.

## Files Expected to Change
- path/to/file.ext — reason
```

---

## Status Section (appended on completion or interruption)

Append this block to the document **before moving it**:

```markdown
---

## Status

**Final Status:** Completed | Incomplete | Blocked
**Track:** main | <track-slug>
**Completed:** YYYY-MM-DD (or N/A)
**Completed By:** [name or agent]

### Outcomes
- What was actually delivered (may differ from plan).

### Deviations
- Any departure from the original approach, and why.

### Lessons Learned
- Non-obvious findings worth carrying forward.

### Backlog Items Created
- planning/backlog/feature_<name>_<date>.md — one-line summary
```

---

## Backlog Items

When planning or execution uncovers work that is real but out of scope for this session, write a stub to `planning/backlog/`. Do not attempt to execute it now.

Minimum content for a backlog stub:

```markdown
# [Item Name]

**Status:** Backlog
**Date Discovered:** YYYY-MM-DD
**Discovered During:** planning/in-progress/feature_<source>_<date>.md

## Context
Why this item exists and what problem it solves.

## Known Scope
What is understood so far. Leave blank if unknown.

## Trigger for Promotion
What event or decision should pull this into active planning.
```

Backlog items stay in `planning/backlog/` until a future session explicitly promotes them: read the stub, expand it into a full plan document, and move it to `planning/` (queued) or `planning/in-progress/` (immediate start).

---

## Parallel Tracks

`in-progress/` holds **one plan per track**, not one plan overall. A track is a
concurrency slot — a channel of work with a single executor — and every plan declares
which one it runs on:

```markdown
**Track:** main
**Track:** content-areas
```

`main` is the foreground session: the default, and the value to use whenever no
sub-agent is involved. Any other value names a track a sub-agent owns for the duration
of the plan.

Name the track after the **work**, not the worker. Sub-agents are ephemeral — one dies,
another resumes the same plan — so an agent id in this field goes stale the moment the
agent does. A stable slug (`engine`, `content-areas`, `infra`) survives handoff and
still discriminates cleanly.

### Admitting a plan to a track

A plan may move into `in-progress/` when both hold:

1. **The track is free.** No other document in `in-progress/` declares the same track.
2. **Its file set is disjoint.** Its `## Files Expected to Change` list shares no path
   with any other in-progress plan's list.

Rule 2 is the one that actually protects the work. The track field is bookkeeping; two
agents editing the same file is the failure it exists to prevent. If the file sets
overlap, the plans are not independent — queue the second one in `planning/` instead of
splitting it across tracks.

```bash
# What is running, and where
grep -H "^\*\*Track:\*\*" planning/in-progress/*.md

# Does the file set collide with anything already running?
grep -hA20 "## Files Expected to Change" planning/in-progress/*.md | grep "^- "
```

### Committing from a track

Each track commits only its own plan document and the code that plan names. Do not
restage another track's files while it is mid-flight — the kanban move and the code it
describes belong in the same commit, and that commit belongs to one track.

### Returning a plan to the queue

An interrupted plan keeps its `Track` value in the Status block — that is the record of
where it ran — but the value is advisory once the document is back in `planning/`.
Reassign it when the plan restarts; the track it used before may be busy or gone.

---

## Adversarial Review Integration (Optional)

When using the plan review pipeline (`plan-send-review`, `plan-intake-review`, `plan-receive-review`), insert a review gate **before** execution starts:

```
planning/ (queued plan)
    │
    ▼
planning/needs-review/      ← plan-send-review deposits here
    │
    ▼
planning/needs-review/reviewed/  ← plan-intake-review returns reviewed plan here
    │
    ▼
Read review feedback → address critical issues → update plan
    │
    ▼
planning/in-progress/       ← now begin execution
```

When used without the review pipeline, move directly from `planning/` to `planning/in-progress/` when execution starts.

---

## Commit Checkpoints

Commit the plan document at each state transition:

| Event | Commit message prefix |
|---|---|
| Plan written to `planning/` | `[PLAN]` |
| Plan moved to `planning/in-progress/` | `[PLAN]` |
| Feature completed (code + plan status updated) | `[FEATURE]` |
| Feature moved to `planning/completed/` | `[FEATURE]` |
| Backlog item created | `[BACKLOG]` |

Example: `[FEATURE] auth-refactor phase 1 complete — JWT validation extracted`

---

## Guardrails

- **One active plan per track** in `planning/in-progress/`. Most work is `Track: main`, so most of the time this reads as one active plan. If a second plan must start urgently on a track that is already busy, move the current plan back to `planning/` with a status note first.
- **Tracks must be independent.** A plan only earns a track of its own if its `Files Expected to Change` set is disjoint from every other in-progress plan's. Overlapping plans queue; they do not parallelise.
- **Never delete plan documents.** Move to `completed/` or back to `planning/`; the history is the corpus.
- **Backlog is not a graveyard.** Each item in `planning/backlog/` should have a `Trigger for Promotion` — if it will never be acted on, note that in the status and leave it.
- **All directories are source-controlled.** Add `.gitkeep` files if directories would otherwise be empty.
