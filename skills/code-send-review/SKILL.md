---
name: code-send-review
description: Use when queueing a slice of a branch for code review — builds a slice from a commit range, focus areas, and plan-doc refs, and drops it in planning/code-reviews/needs-review/ for the intake loop to pick up
metadata:
  category: review-helpers
  order: 20
---
# Send Code Slice for Review

Create a slice from the supplied commit range and focus areas, and drop it in the project's code-review queue so the intake skill can dispatch a reviewer.

**Core principle:** Copy nothing, fabricate nothing. Every SHA, commit, and plan-doc reference must be verified against git and the filesystem before the file is written.

## Naming Convention

Slice filenames follow this format strictly:

```
YYYY-MM-DD-code-review-<initiative>-slice-NN-<slug>.md
```

- `YYYY-MM-DD` — today's date when the slice is queued.
- `code-review` — literal infix identifying the artifact type. Do not shorten or omit.
- `<initiative>` — kebab-case slug identifying the parent initiative / meta-plan (`chat-history-3-0`, `state-machine-recovery`). Slice numbering is **sequential within an initiative**, not across the queue. Different initiatives can reuse slice numbers; the initiative slug is what makes the filename globally unique.
- `slice-NN` — literal infix; `NN` is the two-digit slice number (sequential within the initiative).
- `<slug>` — kebab-case short label for the slice itself.

Example: `2026-04-27-code-review-chat-history-3-0-slice-01-preflight-skeleton.md`

**Legacy form (read only):** `YYYY-MM-DD-code-review-slice-NN-<slug>.md`. These are files written before the `<initiative>` segment existed. The intake skill still reads them, but this skill never writes one.

`code-intake-review` enforces this naming and ignores files that match neither form.

## Frontmatter Convention

A slice is a planning document. The frontmatter contract (`plan-writing-syntax`) and its validator (`plan-doc.cs validate`) cover it, the same way they cover a plan. Required fields:

```yaml
---
kind: slice                # the planning contract's kind; validated
status: queued             # queued | reviewing | failed | reviewed | triaging | completed
date: YYYY-MM-DD           # the day the slice was queued
initiative: <slug>         # required; kebab-case (chat-history-3-0)
parent_plan: <name>        # required; the meta-plan's NAME, never its path
slice: 01                  # zero-padded two-digit; sequential within initiative
title: state-machine-foundation
branch: feature/state-machine
base_sha: <full SHA>
head_sha: <full SHA>
diff_stat: "<N files, +M/-L>"
plan_docs: []              # zero or more plan NAMES under planning/
status_updated: <ISO 8601> # set on every status change
status_note: ""            # optional human-readable reason; populated on failed/triaging
---
```

**`kind: slice` is what keeps the plan-* intake from picking up the file.** It replaces the
retired `type: code-review-slice`. The validator checks `status:` against the directory:

| status | directory |
|---|---|
| `queued` | `planning/code-reviews/needs-review/` |
| `reviewing`, `failed` | `planning/code-reviews/needs-review/in-progress/` |
| `reviewed`, `triaging` | `planning/code-reviews/needs-review/reviewed/` |
| `completed` | `planning/code-reviews/needs-review/completed/` |

**References are names, never paths.** A name is the filename minus `feature_`/`bug_`/`stub_` and
`.md`. The meta-plan moves from `planning/` to `in-progress/` to `completed/` while its slices are
open. A stored path would go stale on the first move, but a name does not. The validator resolves
`parent_plan:` (to a plan) and each `plan_docs:` entry (to a plan or stub). It fails a path with
`PATH-NOT-NAME` and an unknown name with `UNRESOLVED`.

Slices run without a meta-plan when this skill is used directly, as a manual escape hatch. Even
then, `parent_plan:` must name a plan, so write or pick one first. A single-slice review is still
a plan. Plans that live outside the repository, such as `~/.claude/plans/*.md` or Copilot session
state, cannot be resolved by name. Cite those by path in the body's *Plan docs implemented*
section, not in `plan_docs:`.

`initiative` scopes slice numbering. A single branch / parent meta-plan should pick a stable slug (e.g. `chat-history-3-0`) and reuse it for every slice in that initiative. Different initiatives are independent — `chat-history-3-0/slice-01` and `state-machine-recovery/slice-01` can coexist.

The `status` + `status_updated` + `status_note` fields are the **idempotency anchor**. Together with the kanban directories (`needs-review/` → `in-progress/` → `reviewed/` → `completed/`) they encode where the slice is in its lifecycle. `code-send-review` always writes `status: queued` for a newly queued slice; later skills (`code-intake-review`, `code-review-apply`) update the status as the slice progresses.

## Process

### Step 1: Gather Inputs

Required from the user (or derivable from context):

| Input | Notes |
|---|---|
| `initiative` | Kebab-case slug for the parent initiative / meta-plan (`chat-history-3-0`, `state-machine-recovery`). Reuse the same slug for every slice in the same effort. If the user is starting a new initiative, agree on the slug before queueing the first slice — it's a name, not a number, and it persists. |
| `slice_number` | Zero-padded 2-digit integer (`01`, `02`, ...). Sequential **within the initiative** — `chat-history-3-0/slice-01` and `state-machine-recovery/slice-01` can coexist. |
| `slice_name` | Kebab-case short label (`state-machine-foundation`, `sse-broker`). |
| `base_sha` | The commit *before* the slice starts (parent of the first slice commit). |
| `head_sha` | The last commit of the slice. |
| `parent_plan` | The name of the meta-plan (`code-review-<initiative>_<date>`) or other plan this slice reviews for. |
| `plan_docs` | Zero or more names of plans under `planning/` that this slice implements. |
| `focus_areas` | Free-form bullets — what the reviewer should pay attention to. |

If anything is missing, ask before writing. **Especially ask about `initiative`** — guessing this wrong means slices land under the wrong umbrella and synthesis groups them incorrectly.

### Step 2: Verify Against Git

```bash
git rev-parse <base_sha>
git rev-parse <head_sha>
git log --oneline <base_sha>..<head_sha>
git diff --stat <base_sha>..<head_sha> | tail -1
```

If `rev-parse` fails, the slice cannot be created. Stop and ask.

Record the exact commit list and the diff-stat summary — both land in the slice.

### Step 3: Verify the Plan Names

For `parent_plan` and each `plan_docs` entry, confirm that exactly one document under `planning/`
has that name:

```bash
ls planning/{,in-progress/,completed/,backlog/,blocked/,not-implemented/}{,feature_,bug_,stub_}<name>.md 2>/dev/null
```

Zero matches, or more than one, means stop. A broken reference poisons the review, and the
validator would fail it anyway.

### Step 4: Check for Collisions

The collision check is **scoped to the initiative** — different initiatives can reuse slice numbers, so the check matches files whose initiative slug AND slice number both match.

```bash
ls planning/code-reviews/needs-review/*-code-review-<initiative>-slice-<NN>-*.md 2>/dev/null
ls planning/code-reviews/needs-review/in-progress/*-code-review-<initiative>-slice-<NN>-*.md 2>/dev/null
ls planning/code-reviews/needs-review/reviewed/*-code-review-<initiative>-slice-<NN>-*.md 2>/dev/null
ls planning/code-reviews/needs-review/completed/*-code-review-<initiative>-slice-<NN>-*.md 2>/dev/null
```

If `<initiative>/<NN>` already exists anywhere in the queue, stop. Ask whether to pick a new slice number, supersede the existing slice, or correct the initiative slug.

**Legacy collision check.** Also run `ls planning/code-reviews/needs-review/{,in-progress/,reviewed/,completed/}*-code-review-slice-<NN>-*.md 2>/dev/null` once. A match is a legacy slice: it has no initiative segment, probably no `kind:`, and may hold a path in `parent_plan`. If it belongs to a different effort, reusing the number is fine. If it belongs to this one, migrate it first: add `kind: slice`, `status:`, `date:` and `initiative:`, rename it to the initiative form, and turn `parent_plan:` and `plan_docs:` into names.

### Step 5: Write the Slice

Filename: `planning/code-reviews/needs-review/YYYY-MM-DD-code-review-<initiative>-slice-NN-<slug>.md` per the Naming Convention above.

Use the template below verbatim. Fill all placeholders with verified data. Do NOT fabricate commits or SHAs.

```markdown
---
kind: slice
status: queued
date: <YYYY-MM-DD>
initiative: <initiative_slug>
parent_plan: <meta-plan name>
slice: <NN>
title: <slice_name>
branch: <current_branch>
base_sha: <base_sha>
head_sha: <head_sha>
diff_stat: "<N files, +M/-L>"
plan_docs:
  - <plan name>
status_updated: <ISO 8601 timestamp at write time>
status_note: ""
---

# Slice <NN>: <Human-Readable Title>

## Scope

`git diff <base_sha>..<head_sha>` — <N> commits, <diff_stat>.

### Commits

```
<output of git log --oneline base..head, most recent first>
```

### Plan docs implemented

- `<plan name>` — <one-line description>
- `~/.claude/plans/<file>.md` — <for a plan outside the repository, which cannot be named>

(or "None" if no plan doc)

## What was implemented

<2-4 paragraphs the author writes, describing the slice's intent in plain language. Not a commit-by-commit replay — a thematic summary.>

## Focus areas for the reviewer

<Bullets. Be specific. Examples:>
- Plan fidelity: the plan's Plan Review section flagged <X> and <Y> — verify those were addressed.
- Race / reentrancy around <specific handler>.
- State-machine correctness: transitions go through IStateTransitionService, reasons match constants.
- C# coding standards on new files under <path>.

## Known context

<Anything the reviewer needs to know that isn't obvious from the diff: prior slice findings that carried over, temp workarounds with follow-up tickets, dependencies on unlanded work, etc. Optional — omit the section if empty.>

---

<!-- The code-intake-review skill appends a `## Code Review` section below this line. -->
```

### Step 6: Confirm

Report to the user:
- Destination path
- Initiative slug + slice number + name
- Commit count + diff-stat
- Plan docs referenced

Do not mark the slice "done" — the intake loop will do that.

## Do Not

- Guess at SHAs or commit titles — always verify via git.
- Write a slice that references plan docs that don't exist.
- Write a path where a name belongs (`parent_plan`, `plan_docs`).
- Write `type: code-review-slice`. It is retired, and `kind: slice` replaces it.
- Reuse a slice number that's already in the queue **for the same initiative**.
- Invent a new initiative slug for work that belongs to an existing one — initiative slugs persist for the life of the meta-plan.
- Edit the slice after it's in `needs-review/` — let intake move it first.
- Add findings yourself — that's intake's job.

## Red Flags

- "I'll fill the SHA in later." → Stop. Verify first or don't write.
- "The plan doc is close enough." → Stop. Exact path or ask.
- "Slice 03 is already in reviewed/ but this is different work." → That's exactly when to use a different `initiative` slug. The new slice can still be `slice-01` under its own initiative.
- "I'll skip the initiative for now and add it later." → Stop. Ask the user for the slug; do not write the slice without it.
