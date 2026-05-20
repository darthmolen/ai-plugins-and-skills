---
name: code-review-plan-create
description: Use when ready to plan a code review of a development branch — walks the branch, discovers existing plan documents, maps commits to plans, Q&As unmapped commits with the user, and writes a meta-plan with a slice table at planning/YYYY-MM-DD-code-review-plan-<slug>.md
metadata:
  category: review-helpers
  order: 30
---
# Create a Code-Review Meta-Plan

First punch-in command of the code-review workflow. Produces the **meta-plan** that drives slicing — a single document at `planning/YYYY-MM-DD-code-review-plan-<slug>.md` (frontmatter `type: code-review-plan`) with a slice table that the rest of the workflow operates on.

**Core principle:** Git is the source of truth, plans are best-effort context. Commits land in three patterns — clean (matches a plan), revised (matches a post-receive plan version), drifted (matches no plan). This skill maps what it can and Q&As what it can't.

## When to Use

- After implementation work has landed on a branch and you're ready to review it.
- When you want to slice a long-running branch into reviewable chunks.
- When you have a branch with mixed plan-driven and ad-hoc work and need to surface boundaries.

## Process

### Step 1: Identify Branch and Base

Defaults: current branch, base = `origin/main`. Confirm both with the user before walking commits. Edge cases:
- Branch has no upstream → ask which base to use.
- Working tree dirty → warn but proceed; SHAs in the meta-plan are committed-only.
- Detached HEAD → bail, ask user to check out a branch.

### Step 2: Walk Commits

```bash
git log --oneline --reverse <base>..HEAD
git log --pretty=format:"%H%x09%ad%x09%s" --date=short --reverse <base>..HEAD
```

Capture for every commit:
- full SHA, short SHA
- author date (yyyy-mm-dd)
- subject line
- body (for plan-doc references / co-author info)
- file-touch summary (`git log --stat -1 <SHA>`)

### Step 3: Discover Candidate Plan Docs

Scan all of:
```
~/.claude/plans/*.md
~/.copilot/session-state/*/plan.md
<project>/planning/**/*.md
```

For each candidate, extract:
- frontmatter `type` (filter to `plan` or no type — skip `code-review-*` types here)
- frontmatter `slug` if present
- first H1 heading
- file mtime and creation date
- any explicit slug-style file basename

### Step 4: Map Commits → Plans

For each plan candidate, score commits by:

1. **Plan slug or filename in commit subject** — strong match.
2. **Plan filename referenced in commit body** — strong match.
3. **Plan H1 keywords overlapping commit subject** (≥3 non-trivial words) — medium match.
4. **Time-window heuristic**: commits authored within `[plan_creation_date, plan_creation_date + 14 days]` — weak match, used only when other signals tied.
5. **Commit subject prefix match** (e.g., commits prefixed `Phase B1.x:` map to a plan whose body or H1 mentions "Phase B").

A commit may match multiple plans; weakest match wins only if no stronger match exists. Threshold: at least one strong OR two medium matches required to claim a mapping.

### Step 5: Surface to User

Present a draft mapping in chat:

```
Commit mapping draft for <branch> (<base>..HEAD = N commits):

PLAN: <path/to/plan-A.md>
  Score: 23 commits matched (12 strong, 11 medium)
  - <SHA> Phase B1.a: dual-write swarm-phase ...
  - <SHA> Phase B1.b: ...
  ...

PLAN: <path/to/plan-B.md>
  Score: 8 commits matched (5 strong, 3 medium)
  - <SHA> Smart Continue: wire the leader's repair_plan_after_failure tool
  ...

UNMAPPED (12 commits):
  - <SHA> SPA: re-hydrate after recovery actions so SSE reconnects
  - <SHA> Randomize hung-swarm-7e1c8d95.sql by default
  ...
```

### Step 6: Q&A on Unmapped Chunks

For each unmapped chunk:
1. Show the commits with subjects and short SHAs.
2. Ask: "These commits aren't mapped to any plan. Are they:
   - one slice of related work, or
   - multiple slices, or
   - should they be folded into an adjacent mapped plan's slice?"
3. If "one/multiple slices", ask for a name and focus areas for each.
4. If "fold into adjacent", ask which plan and adjust the mapping.

### Step 7: Confirm Slicing

Show the user the proposed slice table:

```
Proposed slices:

| # | Name | Commits | Plan docs | Focus |
|---|---|---|---|---|
| 01 | state-machine-foundation | <base>..<sha> (15 commits) | plan-A.md | dual-write transitions, drop legacy columns |
| 02 | smart-continue            | <sha>..<sha>  (5 commits) | plan-B.md | leader repair tool wiring |
| 03 | recovery-recommendation  | <sha>..<sha>  (8 commits) | plan-C.md, plan-D.md | recommendation surface + orphan defense |
| 04 | demo-seeds-randomization | <sha>..HEAD   (3 commits) | (none — ad-hoc) | seed determinism, GUC pinning |
```

Iterate with user until they say "yes, write it." Allow:
- splitting a slice
- merging two slices
- reordering (must remain chronologically valid: each slice's `base_sha` is its predecessor's `head_sha`)
- renaming
- adjusting focus areas

### Step 8: Write the Meta-Plan

Filename: `planning/YYYY-MM-DD-code-review-plan-<slug>.md`. The `<slug>` is typically the branch name kebab-cased (`feature-state-machine` → `feature-state-machine`), or user-provided.

```markdown
---
type: code-review-plan
branch: <branch>
base_sha: <full SHA>
head_sha: <full SHA>
slug: <slug>
slice_count: <N>
created: YYYY-MM-DD
---

# Code Review Plan — <branch>

## Context

<2-4 paragraphs: what's on the branch, why it's being reviewed now,
known plans and their state (clean / revised / drifted), any constraints.>

## Slice Table

| # | Name | base_sha | head_sha | Plan docs | Focus areas |
|---|---|---|---|---|---|
| 01 | state-machine-foundation | abc1234 | def5678 | planning/<plan-A>.md | • dual-write transitions through IStateTransitionService<br>• drop legacy Phase/Status columns<br>• migration safety |
| 02 | ... | ... | ... | ... | ... |

## Slice Detail

### Slice 01 — state-machine-foundation

**Commits (15):**
```
<git log --oneline base..head for slice 01>
```

**Plan docs:**
- [<plan-A.md>](path/to/plan-A.md) — short context on what the plan covered

**Focus areas:**
- Verify all transitions go through IStateTransitionService (no direct entity writes).
- Confirm legacy column drops are accompanied by data backfill or migration safety net.
- ...

**Known context (optional):**
- Plan went through one round of review-receive; the v2 of the plan is what was implemented.
- Phase B1.k landed late and may show up at slice boundary.

### Slice 02 — ...
[same shape]

## Review Order

Default: 01 → 02 → ... → N. Adjust if some slices logically must precede others.

**When `slice_count > 1`, you MUST include a dependency table** instead of prose. Tables are scannable; prose dependency callouts get skimmed past. Example:

| Slice | Depends on | Why |
|---|---|---|
| 01 | (base) | foundation |
| 02 | 01 | builds on the state machine introduced in 01 |
| 03 | 01, 02 | fixes target paths introduced earlier |
| ... | ... | ... |

Every slice from 02 onward should have at least one dependency row — usually `(NN-1)` at minimum, with additional cross-slice deps called out where they exist (e.g., a UI slice depending on an event-broker slice). If a slice is genuinely independent of all others, put `(none)` and explain why.

## Stopping rules

- Pause after any slice that produces ≥3 Critical findings.
- Pause if `code-review-plan-execute` reports >2 failed slices.
- User may always interrupt and re-evaluate.

## Out of scope

- <if any: e.g. "we are not reviewing the docs commits 4a2469d / 1f80579 — they are documentation-only and listed but not sliced.">
```

### Step 9: Confirm and Notify

Report to chat:
- Path of the meta-plan
- Slice count
- Total commits covered
- Any commits intentionally excluded from slicing
- Suggested next step:
  - **If you want adversarial review of the slicing strategy:** run `plan-send-review` on the meta-plan.
  - **If you trust the slicing:** run `code-review-plan-execute` directly to seed and review.

## Heuristics That Help Discovery

- Look for `Phase X.y` or `[ticket-id]` prefixes — these are usually plan-owned.
- Co-author trailers (`Co-Authored-By:`) often correlate with plan-driven work.
- Commits with bodies referencing `planning/` paths are giveaways.
- Single-commit "Fix:" / "Chore:" entries are usually drift; group them by file/theme.

## Do Not

- Fabricate commits or SHAs.
- Map commits to plans the file system can't confirm.
- Write the meta-plan without user sign-off on the slice table.
- Force every commit into a slice — out-of-scope is fine if the user says so.
- Use a slug already in use elsewhere in `planning/` for `code-review-plan` files.

## Red Flags

| Thought | Reality |
|---|---|
| "Just put every commit in slice 01" | A 60-commit slice is unreviewable. Push back. |
| "I can guess the focus areas" | Ask. Wrong focus areas waste reviewer effort. |
| "The plan looks close enough" | Confirm with the user — close-enough mappings are how drift accumulates silently. |
| "Skip the Q&A — auto-cluster the unmapped" | The Q&A is the value. Auto-clustering is what created the mess. |
