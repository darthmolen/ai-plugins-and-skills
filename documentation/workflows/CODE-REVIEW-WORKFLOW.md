# Code Review Workflow

A file-system-driven, idempotent, multi-agent code review pipeline. Slice specs live in `planning/code-reviews/needs-review/` and move through kanban directories as they're processed. Status field + directory location encode where every slice is at all times.

Pairs with the [Plan Review Workflow](PLAN-REVIEW-WORKFLOW.md) — plan review verifies the spec before you write code; this verifies the code matches the spec and is itself well-formed.

---

## Two paths in, one path out

Pick the path that matches your situation:

### Path A — Ad-hoc slice (review as you go)

You're iterating on a branch and want to queue one chunk of work for review without ceremony.

```
code-send-review     → queues one slice spec in needs-review/
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

Use this when: branch is feature-complete, you want one consolidated `SYNTHESIS.md` before merging, you have multiple slices' worth of work to review at once.

### When you've used both

`code-review-plan-execute` is **resume-aware**. If you've already used path A to ad-hoc-review some slices, then later run path B for a full-branch sweep, execute's pre-flight detects the existing slice specs (matched by `parent_plan` frontmatter) and skips them. Path A and path B coexist on the same branch without duplicating reviews.

---

## The seven skills

| Skill | Role | Where it writes |
|---|---|---|
| `code-review-plan-create` | Walks branch, generates meta-plan with slice table | `planning/<date>-code-review-plan-<slug>.md` |
| `code-review-seed-slices` | Fans out a meta-plan into one slice spec per row | `planning/code-reviews/needs-review/*.md` |
| `code-send-review` | Manually queue one ad-hoc slice (path A) | `planning/code-reviews/needs-review/*.md` |
| `code-intake-review` | Picks up a queued slice, dispatches reviewer subagent, appends findings | `planning/code-reviews/needs-review/{in-progress,reviewed}/` |
| `code-review-plan-execute` | Drives full pipeline: pre-flight → seed → review-loop → synthesize | calls intake + synthesize per slice |
| `code-synthesize-reviews` | Reads all reviewed slices, emits consolidated open-finding action list | `planning/code-reviews/SYNTHESIS.md` |
| `code-review-apply` | Walk findings, classify accept/merge/reject/flag, apply changes | edits code, annotates slice spec |

---

## Kanban directory layout

```
planning/code-reviews/
├── README.md                       (optional, project-specific)
├── SYNTHESIS.md                    (written by code-synthesize-reviews)
└── needs-review/
    ├── *.md                        (status: queued — fresh slices waiting)
    ├── in-progress/
    │   └── *.md                    (status: reviewing | failed)
    ├── reviewed/
    │   └── *.md                    (status: reviewed | triaging)
    └── completed/
        └── *.md                    (status: completed — all findings classified)
```

Each move is a `mv` operation that happens **before** any processing. The file system is the source of truth for which stage every slice is in.

---

## Slice spec naming + frontmatter

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

**Legacy form** (still honored on the read path): `YYYY-MM-DD-code-review-slice-NN-<slug>.md` — files written before `<initiative>` was introduced. Intake matches both forms.

Required frontmatter:

```yaml
---
type: code-review-slice          # discriminator
initiative: <slug>               # required for new files
slice: 01                        # zero-padded two-digit
name: <slice_name>
branch: <git_branch>
base_sha: <full_sha>
head_sha: <full_sha>
diff_stat: "<N files, +M/-L>"
plan_docs:
  - <path>
parent_plan: <path>              # optional but strongly recommended
status: queued                   # state machine — see below
status_updated: <ISO 8601>
status_note: ""                  # populated on failed/triaging
created: YYYY-MM-DD
---
```

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
- **`code-synthesize-reviews`** rebuilds `SYNTHESIS.md` from current reviewed/completed state every run.

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
/code-review-apply                # walk SYNTHESIS findings, fix
```

### "I've been reviewing as I go, now I want a final sweep"

```bash
/code-review-plan-create          # generates meta-plan covering the whole branch
/code-review-plan-execute         # pre-flight detects existing reviewed slices, skips them;
                                  # only new commits get fresh slice specs
/code-synthesize-reviews          # rolls everything (old + new reviews) into SYNTHESIS.md
/code-review-apply                # final triage
```

### "A slice failed mid-review"

```bash
# slice file is in in-progress/ with status: failed
# read status_note to learn why (timeout, validation error, etc.)
# fix the underlying issue
# manually edit frontmatter: status: queued
# move file back: mv in-progress/<file> ./<file>
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

The legacy filename form (no `<initiative>` segment) is honored on read — old slices in `reviewed/` and `completed/` don't need to be migrated. New slices written by `code-send-review` always use the current form.

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
- Document `parent_plan` resolution between source-vs-audit copies (relevant when `plan-receive-review` has been used on the meta-plan).
- Consider promoting `<initiative>` from a filename segment to a real subdirectory layout (`planning/code-reviews/<initiative>/{needs-review,in-progress,...}`). The frontmatter field carries enough info to migrate cleanly when the queue volume justifies it.
