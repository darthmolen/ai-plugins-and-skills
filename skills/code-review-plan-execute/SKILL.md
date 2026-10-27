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
# 1. Self-reviewed meta-plans
find planning/ -name "*.md" -not -path "*/needs-review/*"
# filter to those with frontmatter type: code-review-plan

# 2. Adversarially-reviewed meta-plans
ls planning/needs-review/completed/*.md
# filter to those with frontmatter type: code-review-plan
```

**Why self-reviewed comes first.** The meta-plan the author wrote and checked names the major
cases: the slices, focus areas and design intents that matter most for this branch. The
adversarially reviewed copy is the one that went through the long cross-review, and that review
mostly adds edge cases. When the reviewer got that copy first, it followed it to the letter and
went looking for edge cases before the major ones. With the self-reviewed plan first, the
reviewer covers the major cases before anything else. The adversarial copy is the fallback, and
its findings should already have gone back into the source through `plan-receive-review`.

Match logic:
- **Exactly one in `planning/`:** check Source-vs-Audit (below), then confirm with the user: "Execute on `<title>` from `<path>`?".
- **Multiple in `planning/`:** list them, ask the user to pick. Then run Source-vs-Audit on the chosen one.
- **Zero in `planning/`, exactly one in `completed/`:** "No self-reviewed meta-plan found in `planning/`. Found adversarially-reviewed `<path>`. Its review findings may not have been folded back into a source plan. Execute anyway? [y/N]".
- **Zero in `planning/`, multiple in `completed/`:** list them, ask the user to pick (with the same warning).
- **Zero anywhere:** bail with "No `type: code-review-plan` document found. Run `code-review-plan-create` first."

#### Source-vs-Audit resolution

When the chosen candidate is in `planning/`, also check whether a same-slug, same-`type: code-review-plan` audit copy exists in `planning/needs-review/completed/`. If one does, diff them:

- **No audit copy:** the plan was never sent for adversarial review. Use the source.
- **Identical:** use the source. The review changed nothing.
- **Different:** the source has post-review revisions (per `plan-receive-review`'s "audit copy stays untouched; revisions go to source" rule). Use the **source**, and show the user a one-line diff summary so they can see what the revisions changed (e.g. "Source has 6 changes vs audit copy: Slice 05 path corrected, design intents added to slices 03/04/06, dependency table, ...").

The audit copy is never edited and never executed while a source exists. `plan-receive-review` keeps its audit trail, and execute runs against the plan the author checked.

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

Slices are independent by construction: each has its own SHA range, each reviewer only reads
(`git diff base..head`), and each writes only its own slice file. So the loop runs in two passes.
First it sorts every slice by status. Then it dispatches all the queued slices at once (Step 4a).

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
      → add to the dispatch set (Step 4a)
```

#### Step 4a: Dispatch the Queued Slices in Parallel

```
1. Claim every slice in the dispatch set before dispatching any of them:
   mv planning/code-reviews/needs-review/<file> planning/code-reviews/needs-review/in-progress/<file>
   status: reviewing
   status_updated: <ISO 8601 now>
   status_note: ""

2. Dispatch one reviewer per slice, all at once (in Claude Code: one message
   with one Agent call per slice). Each reviewer is a superpowers:code-reviewer
   subagent given code-intake-review/code-reviewer.md with its placeholders
   filled (code-intake-review Step 5). Reviewers are read-only: they return
   the review as text and never touch the slice file. If the host caps
   concurrent subagents, dispatch in batches of that size. Do not fall back
   to one at a time.

3. As each reviewer returns, the main thread writes that slice, one slice at
   a time:
   - On success:  append the review (code-intake-review Step 6 format),
                  move to reviewed/, set status: reviewed
                  (code-intake-review Step 7)
   - On failure:  leave in in-progress/, set status: failed with a
                  status_note → collect for summary.
                  One failure never cancels the others.
```

**The main thread is the only thing that writes to slice files.** It does the claim before
dispatch and the append, move and status change after each reviewer returns. Do not have the
reviewers run code-intake-review's full logic. That logic claims, appends and moves the file
itself, so a reviewer writing a slice file while the main thread is editing it locks the file,
and the review fails. Parallelism belongs to the reviewing. Writes to slice files stay serial, in
one thread.

Claiming every slice first matters too. If execute crashes mid-dispatch, every claimed slice is
in `in-progress/` with `status: reviewing`, and the stale-run check in Step 4 recovers it on the
next run.

Stream chat updates:

- After dispatch: `Dispatched <N> slice reviews in parallel: <NN>, <NN>, ...`
- As each reviewer returns (in completion order, one whole line per slice so parallel output
  never interleaves):
  - `Slice <NN>/<total> — <name> — reviewed (C/I/M: a/b/c) — <verdict>`
  - `Slice <NN>/<total> — <name> — FAILED: <reason>`

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

Stopping rules tripped (from the meta-plan):
  <e.g. "Slice 01 has 4 Critical findings (threshold 3)" or "none">

Run synthesis now? [y/N/skip]
```

The meta-plan's stopping rules (for example, ≥3 Critical findings in one slice, or >2 failed
slices) are checked here, after all slices have come back, not between slices. Because the reviews
run in parallel, nothing is lost by checking them late. When a rule has tripped, say so above the
prompt so the user can decide whether synthesis is still worth running.

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
- Cancel the other reviewers when one slice fails — let them finish and surface the failure in the summary.
- Touch the meta-plan file (read-only).

## Red Flags

| Thought | Reality |
|---|---|
| "Just retry the failed slice automatically" | The reason matters. Surface it; let the user decide. |
| "Skip pre-flight, I trust the meta-plan" | Branch could have been force-pushed. Verify SHAs every run. |
| "Auto-synthesize after the loop, save a step" | The checkpoint catches degenerate runs. Always confirm. |
| "Process slices one at a time to be safe" | Slices are independent SHA ranges with read-only reviewers and one output file each. Nothing is shared, so there is nothing to collide on. Dispatch them together. |
| "Let each reviewer update its own slice status" | Two writers on one file lock it. Reviewers return text. The main thread does every claim, append, move and status change. |
| "Dispatch as each slice is claimed, it's faster" | Claim every slice first. A crash mid-dispatch must leave every slice recoverable by the stale-run check. |
