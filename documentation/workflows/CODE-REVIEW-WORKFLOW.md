# Code Review Workflow

A file-system-driven, idempotent, multi-agent code review pipeline. Slices live in `planning/code-reviews/needs-review/` and move through kanban directories as they're processed. Status field + directory location encode where every slice is at all times.

Every document the pipeline writes is a planning document under the same frontmatter contract as
a plan (`plan-writing-syntax`), and `plan-doc.cs validate` checks it:

| Document | Kind | Where |
|---|---|---|
| Meta-plan | `kind: plan`, `type: code-review-plan` | on the board: `planning/` → `in-progress/` → `completed/` |
| Slice | `kind: slice` | `planning/code-reviews/needs-review/` and its three subfolders |
| Synthesis | `kind: synthesis` | `planning/code-reviews/` while current, then `needs-review/completed/` |

One contract means one validator catches a slice whose status disagrees with its folder, or a
reference written as a path, and a review's documents retire through the board like any plan
instead of piling up beside it.

Pairs with the [Plan Review Workflow](PLAN-REVIEW-WORKFLOW.md) — plan review verifies the plan before you write code; this verifies the code matches the plan and its feature-spec, and is itself well-formed.

---

## Two paths in, one path out

Pick the path that matches your situation:

### Path A — Ad-hoc slice (review as you go)

You're iterating on a branch and want to queue one chunk of work for review without ceremony.

```
code-send-review     → queues one slice in needs-review/
code-intake-review   → reviewer subagent runs, slice lands in reviewed/
code-review-apply    → walk findings, classify, apply fixes
```

Use this when: you just finished step N of a multi-step branch, want feedback before step N+1, don't want to plan the whole branch yet.

### Path B — Meta-plan-driven (full-branch sweep)

You're about to merge / open a PR and want a complete pre-merge review.

```
code-review-plan-create   → walks branch, generates meta-plan with slice table
code-review-plan-execute  → seeds slices, dispatches reviewers, synthesizes findings
code-review-apply         → walk findings, classify, apply fixes
```

Use this when: branch is feature-complete, you want one consolidated synthesis before merging, you have multiple slices' worth of work to review at once.

### When you've used both

`code-review-plan-execute` is **resume-aware**. If you've already used path A to ad-hoc-review some slices, then later run path B for a full-branch sweep, execute's pre-flight detects the existing slices (matched by the meta-plan name in their `parent_plan` frontmatter) and skips them. Path A and path B coexist on the same branch without duplicating reviews.

---

## The seven skills

| Skill | Role | Where it writes |
|---|---|---|
| `code-review-plan-create` | Walks branch, generates meta-plan with slice table | `planning/code-review-<initiative>_<date>.md` (queued) |
| `code-review-seed-slices` | Fans out a meta-plan into one slice per row | `planning/code-reviews/needs-review/*.md` |
| `code-send-review` | Manually queue one ad-hoc slice (path A) | `planning/code-reviews/needs-review/*.md` |
| `code-intake-review` | Picks up a queued slice, dispatches reviewer subagent, appends findings | `planning/code-reviews/needs-review/{in-progress,reviewed}/` |
| `code-review-plan-execute` | Drives full pipeline: pre-flight → seed → review-loop → synthesize | moves the meta-plan to `in-progress/`; calls intake + synthesize |
| `code-synthesize-reviews` | Reads one initiative's reviewed slices, writes a new open-finding action list and supersedes the last | `planning/code-reviews/<date>-code-review-<initiative>-synthesis-NN.md` |
| `code-review-apply` | Walk findings, classify accept/merge/reject/flag, apply changes; close the initiative when its last slice completes | edits code, annotates slice, completes the meta-plan, retires the synthesis |

---

## Kanban directory layout

```
planning/code-reviews/
├── README.md                       (optional, project-specific; signage, not validated)
├── *-synthesis-NN.md               (kind: synthesis, status: open — at most one per initiative)
└── needs-review/
    ├── *.md                        (status: queued — fresh slices waiting)
    ├── in-progress/
    │   └── *.md                    (status: reviewing | failed)
    ├── reviewed/
    │   └── *.md                    (status: reviewed | triaging)
    └── completed/
        └── *.md                    (slice status: completed; synthesis status: done)
```

Each move is a `git mv` (plain `mv` for a file not yet committed) that happens **before** any processing. The file system is the source of truth for which stage every slice is in, and `plan-doc.cs validate` fails a slice whose `status:` disagrees with its folder.

## Lifecycle of one review

```
code-review-plan-create   meta-plan  → planning/                 (plan, queued)
code-review-plan-execute  meta-plan  → planning/in-progress/     (plan, in-progress, track: code-review-<initiative>)
                          slices     → needs-review/ … reviewed/ (slice, queued … reviewed)
code-synthesize-reviews   synthesis-01 → code-reviews/           (synthesis, open)
code-review-apply         slices     → completed/                (slice, completed)
code-synthesize-reviews   synthesis-02 → code-reviews/; synthesis-01 → completed/ (done, superseded_by)
code-review-apply         last slice completes: synthesis-02 → completed/ (done),
                          meta-plan → planning/completed/ (plan, completed)
```

Synthesis is **append-only**. Each run writes the next `NN`, so no report is ever overwritten,
and the trail of syntheses shows how the open-finding count fell.

---

## Slice naming + frontmatter

Filename pattern (current):

```
YYYY-MM-DD-code-review-<initiative>-slice-NN-<slug>.md
```

| Segment | Purpose |
|---|---|
| `YYYY-MM-DD` | Date the slice was queued |
| `code-review` | Literal infix identifying artifact type |
| `<initiative>` | Kebab-case slug for the parent meta-plan / effort (`chat-history-3-0`, `state-machine-recovery`). Slice numbering is sequential **within an initiative** — different initiatives can reuse `slice-01`. |
| `slice-NN` | Two-digit slice number, sequential within initiative |
| `<slug>` | Short human-readable label for this slice |

**Legacy form:** `YYYY-MM-DD-code-review-slice-NN-<slug>.md`, from before `<initiative>` existed. Intake reads it, flags it for migration, and nothing writes it any more.

Required frontmatter:

```yaml
---
kind: slice                      # validated by plan-doc.cs
status: queued                   # state machine — see below
date: YYYY-MM-DD
initiative: <slug>               # required
parent_plan: <meta-plan name>    # required; a name, never a path
slice: 01                        # zero-padded two-digit
title: <slice_name>
branch: <git_branch>
base_sha: <full_sha>
head_sha: <full_sha>
diff_stat: "<N files, +M/-L>"
plan_docs:
  - <plan name>
status_updated: <ISO 8601>
status_note: ""                  # populated on failed/triaging
---
```

`parent_plan` and `plan_docs` are names (filename minus `feature_`/`bug_`/`stub_` and `.md`), so
they survive the meta-plan moving across the board. The validator resolves them and fails a path
with `PATH-NOT-NAME`.

---

## Status state machine

```
queued       — file is in needs-review/, waiting for intake
reviewing    — intake moved to in-progress/, reviewer subagent running
reviewed     — reviewer succeeded, file moved to reviewed/
failed       — reviewer failed, file in in-progress/, status_note has reason
triaging     — code-review-apply is walking findings (file in reviewed/)
completed    — all findings classified, file moved to completed/
```

Transitions are idempotent. The `status_updated` timestamp lets execute detect stale `reviewing` runs (>30 min without progress = sibling executor crashed).

---

## Idempotency

Every skill in the workflow is safe to re-run:

- **`code-send-review`** refuses to overwrite an existing `<initiative>/<slice>` slot.
- **`code-intake-review`** moves before processing; a second invocation finds the directory empty.
- **`code-review-plan-execute`** treats already-processed slices (`reviewed`, `triaging`, `completed`) as no-ops; only processes `queued`. `failed` is surfaced in the summary, never auto-retried.
- **`code-review-apply`** marks `triaging` while walking findings; resuming picks up where you left off.
- **`code-synthesize-reviews`** writes the next numbered synthesis from current reviewed/completed state and supersedes the previous one. Nothing is overwritten.

The state machine is the contract. Re-running a skill never loses findings, never duplicates work.

---

## Common scenarios

### "I just finished a chunk of work, queue it for review"

```bash
# In your dev session
git commit ...                    # finish the chunk
/code-send-review <args>          # queue slice — will ask for initiative slug + slice NN
# In another session (or after a context switch)
/code-intake-review               # reviewer runs, slice → reviewed/
/code-review-apply                # walk findings, fix
```

### "I want to review the whole branch before merging"

```bash
/code-review-plan-create          # walks branch, drops meta-plan in planning/
# (optional adversarial review of the meta-plan via plan-send-review / plan-receive-review)
/code-review-plan-execute         # seeds + reviews everything, ends with synthesis prompt
/code-review-apply                # walk the synthesis findings, fix; closes the review when done
```

### "I've been reviewing as I go, now I want a final sweep"

```bash
/code-review-plan-create          # generates meta-plan covering the whole branch
/code-review-plan-execute         # pre-flight detects existing reviewed slices, skips them;
                                  # only new commits get fresh slices
/code-synthesize-reviews          # rolls the initiative's reviews into the next synthesis
/code-review-apply                # final triage
```

### "A slice failed mid-review"

```bash
# slice file is in in-progress/ with status: failed
# read status_note to learn why (timeout, validation error, etc.)
# fix the underlying issue
# manually edit frontmatter: status: queued
# move file back: git mv in-progress/<file> ./<file>
/code-review-plan-execute         # picks it up on next run
```

### "I need to abort a stuck reviewer"

If a slice has been `status: reviewing` for >30 min and no progress is happening:

```bash
# code-review-plan-execute will ask you on next run:
#   "Found stale reviewing: <file>. Resume / reset / skip?"
# choose "reset" → status flips back to queued, gets re-reviewed
```

---

## Multi-initiative coexistence

A single `planning/code-reviews/` queue can carry slices from multiple parent meta-plans (= initiatives) at once. Examples in the wild:

```
needs-review/
├── 2026-04-25-code-review-state-machine-recovery-slice-08-orphan-defense.md
└── 2026-04-27-code-review-chat-history-3-0-slice-01-preflight-skeleton.md
```

Both can be queued, reviewed, applied independently. The `initiative` slug in the filename + frontmatter is what disambiguates them. `code-review-apply`'s multi-slice picker groups by initiative when listing.

Each initiative has its own syntheses (`…-<initiative>-synthesis-NN.md`) and closes on its own
when its last slice completes.

Legacy documents (no `kind:`, or the no-initiative filename) fail `plan-doc.cs validate`, and the
skills will not synthesize or close them. Migrate a queue once:

1. **Each slice:** add `kind: slice`, `date:` (from `created:`), and `initiative:` (from the
   filename). Turn `parent_plan:` into the meta-plan's name and each `plan_docs:` entry into a plan
   name. Remove `type: code-review-slice`. Rename `name:` to `title:`.
2. **Each meta-plan:** `git mv` it to `code-review-<initiative>_<date>.md` in the board column
   that matches its state. Add `kind: plan`, `status:` and `date:`. Rename `slug:` to
   `initiative:`. Keep `type: code-review-plan`.
3. **Each old `SYNTHESIS.md`:** add `kind: synthesis`, `status: done`, `date:`, `initiative:` and
   `parent_plan:`, then `git mv` it to `needs-review/completed/`. Keep the filename.

---

## What this workflow is NOT

- **Not a CI gate.** Findings are advisory; the developer decides accept/merge/reject during apply.
- **Not a replacement for human review.** It's a pre-PR checklist generator. Use it to catch what's mechanical before a human spends time on the harder questions.
- **Not synchronous.** Intake and execute are designed to run async (often on a `/loop` interval). Slices spend time in `needs-review/` and `in-progress/` between phases. That's expected.
- **Not for one-line bug fixes.** The ceremony is overhead; just commit and move on. Use this for slices ≥10 LOC that touch architecturally interesting code.

---

## Future work

- Add a top-level architecture diagram (state machine + directory transitions).
- Document the `code-reviewer` subagent template at [../../skills/code-intake-review/code-reviewer.md](../../skills/code-intake-review/code-reviewer.md) — it's how findings get the `Status at HEAD` field.
- Harden the skills for git worktrees.
- Consider promoting `<initiative>` from a filename segment to a real subdirectory layout (`planning/code-reviews/<initiative>/{needs-review,in-progress,...}`). The frontmatter field carries enough info to migrate cleanly when the queue volume justifies it.
