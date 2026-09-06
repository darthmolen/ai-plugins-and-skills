---
name: plan-workflow
description: Manages planning documentation through the kanban board at planning/. Use when creating, progressing, or completing plans to ensure every document lands in the right directory (planning/, in-progress/, completed/, not-implemented/, blocked/, backlog/, reminders/, waves/, evidence/, needs-review/) and stays in source control alongside the code it describes. Also covers parallel tracks — one in-progress plan per track when independent plans run in sub-agents. Pairs with plan-writing-syntax, which owns what is written inside the document; this owns where the document lives and when it moves.
metadata:
  category: workflow-composers
---

# Plan Workflow

This skill is the base layer of all spec-driven work. It governs how planning documents move through the kanban directories at `planning/` — whether you are working solo (simple spec-driven) or feeding into the adversarial review pipeline (complex spec-driven). Every layer of the workflow builds on top of this one; the kanban and commit discipline here apply regardless of which path you are on.

The two layers:

- **Simple spec-driven** — use this skill alone to author + execute the plan; no *plan-review* second session. The code-review caboose still applies to anything that ships (the review floor) — see the `spec-tier` skill.
- **Complex spec-driven (adversarial)** — use this skill plus `plan-send-review`, `plan-intake-review`, and `plan-receive-review`. The review trio inserts a gate into the same kanban flow before execution starts; the directories and commit discipline here still apply throughout.

**This skill owns *where a document lives and when it moves*. `plan-writing-syntax` owns *what is written on it*** — the frontmatter, the status vocabularies, and cross-references. Read that one before writing a document; read this one before moving it.

## Directory Map

```
planning/
├── (root)             # Queued: written, ready to start, nobody has started it
├── in-progress/       # Executing — one per track (see Parallel Tracks)
├── completed/         # Finished, with a ## Status block
├── not-implemented/   # Ended without being built — the second terminal state
├── blocked/           # Cannot proceed until something outside the repository clears
├── backlog/           # Stubs: discovered work needing its own planning session
├── reminders/         # Tasks a person must do; completed/ underneath
├── waves/             # Scheduling documents across many plans
├── evidence/          # Artifacts a plan produced and its Status block cites
└── needs-review/      # The review pipeline's copies — never the plan of record
```

Every directory is committed to source control. **No document is ever deleted — only moved.**

That rule has been broken once, and it is worth knowing how: a stub was deleted when its plan was promoted, which left the plan pointing at a file that did not exist. Nobody noticed for a week. Promotion is not deletion — mark the stub `promoted` and leave it where it is.

### `not-implemented/` — the second terminal state

For a plan that **built nothing**: superseded, folded into another plan, obsoleted, or tried and abandoned.

It exists because both alternatives lie. `completed/` is wrong — a reader counting it to see what the system does would count a plan that did not. The queue root is worse: it means *ready to start*, so a dead plan sitting there is a permanent false positive on "what is next".

**An abandoned approach is knowledge.** The next person to have the same idea should find the document saying it was tried and why it was rejected, which is the whole reason the document is kept rather than dropped.

### The directory *is* the status, and both are written down

A document's `status:` field and the directory it sits in have to agree. That is redundancy on purpose — `ls planning/backlog/` reading as a status board is a real affordance a field alone does not give back — and where a repository has a validator, disagreement is a build failure rather than a discovery six weeks later.

**So a move is two edits, not one:** `git mv` the file, and change its `status:` to match. Doing one without the other is the drift this arrangement exists to catch.

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
          ├─ Ended without being built →
          │    1. Append a ## Status section saying which of the four reasons, and
          │       naming what it points at (superseded / folded-in / obsoleted /
          │       abandoned — see plan-writing-syntax).
          │    2. Move the document to planning/not-implemented/.
          │
          ├─ Cannot proceed until something outside the repository changes →
          │    1. Record what is being waited on, by name.
          │    2. Move the document to planning/blocked/.
          │
          └─ Item discovered during planning, outside current scope →
               Write a new document to planning/backlog/ (see Backlog section).
```

**Every one of those moves changes the `status:` field too.** See *The directory is the status* above.

---

## File Naming

Use the format: `feature_<name>_<YYYY-MM-DD>.md`

Examples:
- `planning/feature_auth-refactor_2026-06-22.md`
- `planning/in-progress/feature_auth-refactor_2026-06-22.md`
- `planning/completed/feature_auth-refactor_2026-06-22.md`
- `planning/backlog/feature_session-token-storage_2026-06-22.md`

**Keep the same filename as the document moves between directories.** The name is the document's identity and the directory is its status, which is what lets another document reference it by name and go on resolving after it moves.

The one exception is a **backlog stub**, whose prefix carries its status: `feature_` while open, `promoted_` once a plan grew out of it, `closed_` when it ended. Those are renames, and the prefix has to agree with the `status:` field.

---

## Plan Document Structure

**The frontmatter block belongs to `plan-writing-syntax`** — `kind`, `status`, `track`, `date`, and whatever the status requires. Read that skill for the vocabularies; this is only where it sits.

Under it, a plan's body:

```markdown
---
kind: plan
status: queued
track: main
date: YYYY-MM-DD
---

# [Feature / Bug / Task Name]

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

## Status Section (appended whenever a plan leaves `in-progress/`)

Append this block to the document **before moving it**, and update the frontmatter `status:` in the same edit.

**Every departure from `in-progress/` gets one, not only the terminal ones.** A plan returned to the queue half-done is exactly the one whose Status block matters most — it is the only record of where the work stopped and what the next session is walking into. `Final Status: Incomplete` is a real value.

```markdown
---

## Status

**Final Status:** Completed | Incomplete | Not Implemented | Blocked
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
- <name>_<date> — one-line summary
```

**Write it in the minutes after finishing, not from a diff a week later.** The deviations and the lessons are the half nothing else in the repository records, and they are the half that is reconstructed worst.

**A completed plan is not a to-do list.** Work that was in scope and did not land leaves as a backlog stub with a name, not as a "still to do" section inside a document filed under `completed/`.

---

## Backlog Items

When planning or execution uncovers work that is real but out of scope for this session, write a stub to `planning/backlog/`. Do not attempt to execute it now.

Minimum content for a backlog stub:

```markdown
---
kind: stub
status: open
date: YYYY-MM-DD
---

# [Item Name]

**Discovered During:** <source-plan-name>_<date>

## Context
Why this item exists and what problem it solves.

## Known Scope
What is understood so far. Leave blank if unknown.

## Trigger for Promotion
What event or decision should pull this into active planning.
```

Backlog items stay in `planning/backlog/` until a future session explicitly promotes them: read the stub, expand it into a full plan document, and put **the plan** in `planning/` (queued) or `planning/in-progress/` (immediate start).

**Promotion does not move or delete the stub.** It stays in `backlog/`, renamed to `promoted_`, with `status: promoted` and a `promoted_to:` naming the plan it became. The stub is the record of when the problem was first seen; the plan is the record of what was done about it, and they are different facts.

A stub that ends without becoming a plan is renamed `closed_` with `status: closed`, and says what closed it.

---

## Parallel Tracks

`in-progress/` holds **one plan per track**, not one plan overall. A track is a
concurrency slot — a channel of work with a single executor — and every plan declares
which one it runs on:

```yaml
track: main
track: content-areas
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
grep -H "^track:" planning/in-progress/*.md

# Does the file set collide with anything already running?
grep -hA20 "## Files Expected to Change" planning/in-progress/*.md | grep "^- "

# Where a validator exists, it parses this rather than grepping it
npm run validate:plans
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
| Plan moved to `planning/not-implemented/` | `[PLAN]` |
| Plan moved to `planning/blocked/` | `[PLAN]` |
| Backlog item created | `[BACKLOG]` |

Example: `[FEATURE] auth-refactor phase 1 complete — JWT validation extracted`

---

## Guardrails

- **One active plan per track** in `planning/in-progress/`. Most work is `track: main`, so most of the time this reads as one active plan. If a second plan must start urgently on a track that is already busy, move the current plan back to `planning/` with a status note first.
- **Tracks must be independent.** A plan only earns a track of its own if its `Files Expected to Change` set is disjoint from every other in-progress plan's. Overlapping plans queue; they do not parallelise.
- **Never delete plan documents.** Move them; the history is the corpus. **Promotion is not deletion** — deleting a stub on promotion is how one plan came to point at a file that did not exist.
- **A move is two edits.** `git mv`, and the `status:` field. One without the other is exactly the drift the field was introduced to end.
- **Backlog is not a graveyard.** Each stub should have a `Trigger for Promotion` — if it will never be acted on, close it with a reason rather than leaving it open forever.
- **All directories are source-controlled.** Add `.gitkeep` files if directories would otherwise be empty.
- **`needs-review/` never holds the plan of record.** Its contents are copies for the review pipeline; the plan itself stays on the board.
