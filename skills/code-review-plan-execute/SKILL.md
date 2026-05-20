---
name: code-review-plan-execute
description: Use when a code-review meta-plan is ready and you want to drive the full review pipeline — pre-flight, seed slices, dispatch reviewers per slice, synthesize findings, and notify on completion; defensive and idempotent so repeated runs resume from where the last run stopped
metadata:
  category: review-helpers
  order: 32
---
# Execute a Code-Review Meta-Plan

Second punch-in command. Orchestrates the full review pipeline: pre-flight → seed → review-loop → synthesize → notify.

**Core principle:** Defensive and idempotent. Re-running this skill against the same meta-plan should never duplicate work, never lose findings, and always make forward progress. Status field + kanban directories together encode where every slice is.

## When to Use

- After `code-review-plan-create` produced a meta-plan and you're ready to start the review.
- To resume a partially-completed run (slices in `in-progress/`, `reviewed/`, or `completed/`).
- To re-attempt failed slices after fixing the underlying issue (e.g., reviewer timeout was an env problem; restart the slice).

## Process

### Step 1: Find the Meta-Plan

Search order:

```bash
# 1. Adversarially-reviewed meta-plans
ls planning/needs-review/completed/*.md
# filter to those with frontmatter type: code-review-plan

# 2. Self-reviewed meta-plans
find planning/ -name "*.md" -not -path "*/needs-review/*"
# filter to those with frontmatter type: code-review-plan
```

Match logic:
- **Exactly one in `completed/`:** before settling on it, check Source-vs-Audit (below). Otherwise confirm with user "Execute on `<title>` from `<path>`?".
- **Multiple in `completed/`:** list them, ask user to pick. Then run Source-vs-Audit on the chosen one.
- **Zero in `completed/`, exactly one in `planning/`:** "No adversarially-reviewed meta-plan found. Found `<path>` in `planning/` (not adversarially reviewed). Execute anyway? [y/N]".
- **Zero in `completed/`, multiple in `planning/`:** list, ask user to pick (with same not-adversarially-reviewed warning).
- **Zero anywhere:** bail with "No `type: code-review-plan` document found. Run `code-review-plan-create` first."

#### Source-vs-Audit resolution

When a candidate is in `completed/`, also check whether a same-slug, same-`type: code-review-plan` file exists at the source location (`planning/<filename>`). If yes, diff them:

- **Identical:** use the `completed/` copy. Normal case.
- **Different:** the source has post-review revisions (per `plan-receive-review`'s "audit copy stays untouched; revisions go to source" rule). Prefer the **source**, surface a one-line diff summary to the user (e.g. "Source has 6 changes vs audit copy: Slice 05 path corrected, design intents added to slices 03/04/06, dependency table, ..."), and confirm:
  > "Use revised source `<path>`? Audit copy is unchanged at `<completed/path>`. [y/N]"

This keeps `plan-receive-review`'s audit-trail discipline intact while letting execute run against the actionable post-revision version.

### Step 2: Pre-Flight

For the chosen meta-plan, validate:

1. **Frontmatter sanity:** `type: code-review-plan`, `branch`, `base_sha`, `head_sha`, `slug`, `slice_count` all present.
2. **Slice table parseable:** matches the meta-plan's declared `slice_count`.
3. **SHAs verify:**
   ```bash
   git rev-parse <base_sha>
   git rev-parse <head_sha>
   # for each slice row:
   git rev-parse <slice.base_sha>
   git rev-parse <slice.head_sha>
   ```
   Any failure = the branch moved destructively (force-push, rebase, etc.); abort with clear message.
4. **Branch state:** working tree clean, not mid-rebase, not detached. Warn but proceed if working tree has untracked-only changes (uncommitted modifications = abort).
5. **Plan docs referenced exist on disk** for every slice row.
6. **Collision check (per-slice):** for each slice number in the meta-plan, scan `planning/code-reviews/needs-review/{,in-progress/,reviewed/,completed/}` for an existing slice spec. Allowed cases:
   - **No file exists:** OK, will be seeded.
   - **File exists with `parent_plan` matching this meta-plan:** OK, **resume** mode (skip seeding for this slice; existing status determines what happens next).
   - **File exists with `parent_plan` matching a *different* meta-plan, or no `parent_plan`:** ABORT — collision is unresolvable without user direction.

Print the pre-flight summary and ask user "Proceed? [y/N]".

### Step 3: Seed Slices (skipping resume cases)

Invoke `code-review-seed-slices` on the meta-plan. The seed skill writes one slice spec per row to `planning/code-reviews/needs-review/` with `status: queued` (skipping any that already exist from resume cases).

Stream chat output:
- "Seeded N slices in `needs-review/` (M skipped — already present from prior run)."

### Step 4: Review Loop

Read all slice specs across `needs-review/`, `in-progress/`, `reviewed/`, `completed/` matching the meta-plan's `slug`. Process each by status:

```
For each slice in (numerical order from meta-plan):

  switch status:
    case completed:    skip silently
    case triaging:     skip silently (apply skill owns this)
    case reviewed:     skip silently (already done)
    case failed:       collect for end-of-run summary; do NOT auto-retry
    case reviewing:
      check status_updated age:
        > 30 minutes  → ask user: resume / reset / skip?
                        - resume: assume reviewer is still running elsewhere; skip
                        - reset:  set status: queued, status_note: "reset by execute"; treat as queued below
                        - skip:   leave alone, surface in summary
        ≤ 30 minutes  → assume sibling executor; skip silently
    case queued:
      → process this slice (Step 4a)
```

#### Step 4a: Process a Queued Slice

```
1. Move file to in-progress/:
   mv planning/code-reviews/needs-review/<file> planning/code-reviews/needs-review/in-progress/<file>

2. Update frontmatter:
   status: reviewing
   status_updated: <ISO 8601 now>
   status_note: ""

3. Dispatch the reviewer by invoking code-intake-review's logic for this single
   file (the intake skill handles subagent dispatch + finding append + status
   transition to reviewed | failed).

4. After code-intake-review returns:
   - On success:  file is in reviewed/ with status: reviewed
                  → continue to next slice
   - On failure:  file is in in-progress/ with status: failed
                  → collect for summary, continue to next slice
```

Stream chat updates per slice:

- `Slice <NN>/<total> — <name> — reviewing...`
- `Slice <NN>/<total> — <name> — reviewed (C/I/M: a/b/c) — <verdict>`
- `Slice <NN>/<total> — <name> — FAILED: <reason> — continuing`

### Step 5: Synthesis Checkpoint

After the review loop completes, summarize:

```
Review loop complete:
- Reviewed:  <N> slice(s)
- Failed:    <M> slice(s) — see in-progress/ with status: failed
- Skipped:   <K> slice(s) — already completed/triaging/reviewed before this run

Findings totals (from reviewed slices):
  Critical:   <c>
  Important:  <i>
  Minor:      <m>

Run synthesis now? [y/N/skip]
```

- **y:** invoke `code-synthesize-reviews` → produces `planning/code-reviews/SYNTHESIS.md`. Report path + open-finding totals.
- **N or skip:** stop here, leave a chat note: "Synthesis skipped. Run `code-synthesize-reviews` later when you're ready."

The checkpoint is **mandatory** — never auto-synthesize without user confirmation. This is the user's chance to spot a degenerate run (e.g., 8/8 slices failed) before generating a misleading SYNTHESIS.md.

### Step 6: Notify

Final chat-only summary:

```
Code review run complete for <meta-plan slug>.
- Slices reviewed:  <N>
- Slices failed:    <M>
- Open findings:    <C/I/M>
- SYNTHESIS.md:     <path or "skipped">

Next step: run `code-review-apply` to triage findings.
```

If any slice failed, surface the per-slice reasons in the summary — the user needs to know what to fix before re-running execute.

## Idempotency Guarantees

- **Re-running on a clean run:** all slices in `completed/` → no work done, just status report.
- **Re-running on a partial run:** completed/triaging/reviewed slices skipped; queued slices processed; failed slices left alone (user must reset to retry).
- **Re-running after resetting a failed slice:** the reset slice is processed exactly once; other completed slices untouched.
- **Re-running with a moved branch:** SHA pre-flight catches it and aborts before any state changes.

## What Execute Does NOT Do

- **Apply findings.** That's `code-review-apply`'s job.
- **Decide whether a finding is valid.** That's the developer's call during apply.
- **Auto-retry failed slices.** Failure indicates a problem the user should look at before re-running.
- **Modify the meta-plan.** The meta-plan is read-only here.
- **Run on its own loop.** This is a foreground command; user invokes when they're ready to drive a review pass.

## Do Not

- Skip the synthesis checkpoint.
- Auto-retry `failed` slices without user direction.
- Re-seed a slice that already has `parent_plan` matching this meta-plan (that's a duplication bug).
- Bail mid-loop on a single slice failure — continue and surface in summary.
- Touch the meta-plan file (read-only).

## Red Flags

| Thought | Reality |
|---|---|
| "Just retry the failed slice automatically" | The reason matters. Surface it; let the user decide. |
| "Skip pre-flight, I trust the meta-plan" | Branch could have been force-pushed. Verify SHAs every run. |
| "Auto-synthesize after the loop, save a step" | The checkpoint catches degenerate runs. Always confirm. |
| "Process slices in parallel for speed" | Sequential keeps chat output legible and avoids subagent collision. |
