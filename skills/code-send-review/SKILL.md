---
name: code-send-review
description: Use when queueing a slice of a branch for code review — builds a slice spec from a commit range, focus areas, and plan-doc refs, and drops it in planning/code-reviews/needs-review/ for the intake loop to pick up
metadata:
  category: review-helpers
  order: 20
---
# Send Code Slice for Review

Create a slice-review spec from the supplied commit range and focus areas, and drop it in the project's code-review queue so the intake skill can dispatch a reviewer.

**Core principle:** Copy nothing, fabricate nothing. Every SHA, commit, and plan-doc reference must be verified against git and the filesystem before the file is written.

## Naming Convention

Slice spec filenames follow this format strictly:

```
YYYY-MM-DD-code-review-<initiative>-slice-NN-<slug>.md
```

- `YYYY-MM-DD` — today's date when the spec is queued.
- `code-review` — literal infix identifying the artifact type. Do not shorten or omit.
- `<initiative>` — kebab-case slug identifying the parent initiative / meta-plan (`chat-history-3-0`, `state-machine-recovery`). Slice numbering is **sequential within an initiative**, not across the queue. Different initiatives can reuse slice numbers; the initiative slug is what makes the filename globally unique.
- `slice-NN` — literal infix; `NN` is the two-digit slice number (sequential within the initiative).
- `<slug>` — kebab-case short label for the slice itself.

Example: `2026-04-27-code-review-chat-history-3-0-slice-01-preflight-skeleton.md`

**Legacy form (still honored on the read path):** `YYYY-MM-DD-code-review-slice-NN-<slug>.md` — files written before the `<initiative>` segment was introduced. The intake skill matches both forms; new files written by `code-send-review` always use the new form with an explicit initiative.

This naming is enforced by `code-intake-review` — files that don't match either form are not picked up.

## Frontmatter Convention

Every slice spec carries a YAML frontmatter block. Required fields:

```yaml
---
type: code-review-slice    # discriminator — required
initiative: <slug>         # required for new files — kebab-case (chat-history-3-0)
slice: 01                  # zero-padded two-digit; sequential within initiative
name: state-machine-foundation
branch: feature/state-machine
base_sha: <full SHA>
head_sha: <full SHA>
diff_stat: "<N files, +M/-L>"
plan_docs: []              # zero or more paths
parent_plan: <path>        # optional: link to the meta-plan that spawned this slice
status: queued             # queued | reviewing | reviewed | failed | triaging | completed
status_updated: <ISO 8601> # set on every status change
status_note: ""            # optional human-readable reason; populated on failed/triaging
created: YYYY-MM-DD
---
```

`type: code-review-slice` is the discriminator that prevents the plan-* intake from picking up the file. It MUST be present.

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
| `plan_docs` | Zero or more paths to plan markdown files this slice implements. |
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

Record the exact commit list and the diff-stat summary — both land in the spec.

### Step 3: Verify Plan-Doc Paths

For each `plan_docs` entry, confirm the file exists. If it doesn't, stop — a broken reference poisons the review.

### Step 4: Check for Collisions

The collision check is **scoped to the initiative** — different initiatives can reuse slice numbers, so the check matches files whose initiative slug AND slice number both match.

```bash
ls planning/code-reviews/needs-review/*-code-review-<initiative>-slice-<NN>-*.md 2>/dev/null
ls planning/code-reviews/needs-review/in-progress/*-code-review-<initiative>-slice-<NN>-*.md 2>/dev/null
ls planning/code-reviews/needs-review/reviewed/*-code-review-<initiative>-slice-<NN>-*.md 2>/dev/null
ls planning/code-reviews/needs-review/completed/*-code-review-<initiative>-slice-<NN>-*.md 2>/dev/null
```

If `<initiative>/<NN>` already exists anywhere in the queue, stop. Ask whether to pick a new slice number, supersede the existing spec, or correct the initiative slug.

**Legacy collision check (ambient).** Also confirm the slice number doesn't clash with a *legacy-format* file (no initiative segment) for the same kind of work. Run `ls planning/code-reviews/needs-review/{,in-progress/,reviewed/,completed/}*-code-review-slice-<NN>-*.md 2>/dev/null` once. If any matches appear, scan their `parent_plan` frontmatter — if the legacy slice belongs to a *different* initiative, it's fine to reuse; if it's the same initiative, the legacy slice must be migrated to the new naming first.

### Step 5: Write the Slice Spec

Filename: `planning/code-reviews/needs-review/YYYY-MM-DD-code-review-<initiative>-slice-NN-<slug>.md` per the Naming Convention above.

Use the template below verbatim. Fill all placeholders with verified data. Do NOT fabricate commits or SHAs.

```markdown
---
type: code-review-slice
initiative: <initiative_slug>
slice: <NN>
name: <slice_name>
branch: <current_branch>
base_sha: <base_sha>
head_sha: <head_sha>
diff_stat: "<N files, +M/-L>"
plan_docs:
  - <path>
parent_plan: <path or omit>
status: queued
status_updated: <ISO 8601 timestamp at write time>
status_note: ""
created: <YYYY-MM-DD>
---

# Slice <NN>: <Human-Readable Title>

## Scope

`git diff <base_sha>..<head_sha>` — <N> commits, <diff_stat>.

### Commits

```
<output of git log --oneline base..head, most recent first>
```

### Plan docs implemented

- [<basename>](<path>) — <one-line description>

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
- Write a spec that references plan docs that don't exist.
- Reuse a slice number that's already in the queue **for the same initiative**.
- Invent a new initiative slug for work that belongs to an existing one — initiative slugs persist for the life of the meta-plan.
- Edit the spec after it's in `needs-review/` — let intake move it first.
- Add findings yourself — that's intake's job.

## Red Flags

- "I'll fill the SHA in later." → Stop. Verify first or don't write.
- "The plan doc is close enough." → Stop. Exact path or ask.
- "Slice 03 is already in reviewed/ but this is different work." → That's exactly when to use a different `initiative` slug. The new slice can still be `slice-01` under its own initiative.
- "I'll skip the initiative for now and add it later." → Stop. Ask the user for the slug; do not write the spec without it.
