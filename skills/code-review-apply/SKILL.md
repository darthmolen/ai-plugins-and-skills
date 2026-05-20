---
name: code-review-apply
description: Use when reviewed code-review slices are ready for triage and the developer wants to walk findings, classify them accept/merge/reject/flag, and apply accepted changes to code — third punch-in command of the code review workflow, after code-review-plan-execute completes
metadata:
  category: review-helpers
  order: 22
---
# Code Review Apply

Walk the open findings from reviewed code-review slices, classify each one with technical rigor, and apply the accepted/merged ones to the codebase.

**Core principle:** Same as `superpowers:receiving-code-review` — verify before implementing. No performative agreement. Technical rigor always. The slice spec is the audit trail; never delete a reviewer's words, only annotate `Status at HEAD`.

## When to Use

After `code-review-plan-execute` finishes — slices are in `planning/code-reviews/needs-review/reviewed/` with `status: reviewed`. Or any time you want to triage findings on a single slice that's been reviewed (e.g., one that was queued via the manual `code-send-review` escape hatch).

## Process

### Step 1: Find Slices to Apply

Slice spec filenames may be in either of two forms — current (`*-code-review-<initiative>-slice-NN-*.md`) or legacy (`*-code-review-slice-NN-*.md`). Both are honored.

```bash
ls planning/code-reviews/needs-review/reviewed/*-code-review-*-slice-*.md \
   planning/code-reviews/needs-review/reviewed/*-code-review-slice-*.md 2>/dev/null \
   | sort -u
```

Filter to those with `status: reviewed`. (Skip `triaging` files unless the user is explicitly resuming a stopped session — see Step 2.)

- **No files:** "No reviewed slices to apply. Run `code-review-plan-execute` first if you have queued slices."
- **One file:** Confirm with user: "Apply findings from `<filename>`?"
- **Multiple files:** Show list grouped by `initiative` (frontmatter), ask user: "All slices in order?", "Pick one initiative?", or "Pick one slice?". Initiative grouping makes the choice clearer when slices from multiple efforts are queued together.

### Step 2: Resume Check

If any file in `reviewed/` already has `status: triaging`:
- Alert user: "Slice `<filename>` is mid-triage (status_updated: <timestamp>). Resume, restart, or skip?"
- Resume: continue where the open findings list left off (re-read; recompute open).
- Restart: reset `status: reviewed` and start over from the first finding.
- Skip: leave it for now.

### Step 3: Mark Triaging

Before walking findings, update the slice's frontmatter:

```yaml
status: triaging
status_updated: <ISO 8601 now>
status_note: ""
```

The file stays in `reviewed/` while triaging.

### Step 4: Walk Findings

Read the `## Code Review` section. Iterate by severity (Critical → Important → Minor). For each finding with `Status at HEAD: open` or `Status at HEAD: partially-addressed-at-*`:

```
For each open finding:
  1. PRESENT to user (verbatim from reviewer):
     - ID, severity, file:line
     - Description
     - Suggested fix
     - Reviewer's Status at HEAD annotation (open or partially-...)

  2. VERIFY against current code:
     - Read the file at the cited line
     - Confirm the issue still exists (don't trust Status at HEAD blindly —
       the code may have moved since the reviewer wrote the finding)

  3. ASK the developer to classify:
     - accept   — apply the suggested fix as written
     - merge    — apply with adaptation; user describes the variant
     - reject   — finding is wrong or doesn't apply; capture why
     - flag     — needs more thought; leave open with annotation
     - defer    — valid but punted to a separate ticket / follow-up branch

  4. ACT on the classification:
     - accept / merge → edit the code; verify (compile/lint/test if quick);
       update the finding's "Status at HEAD" line to:
         "Status at HEAD: resolved-at-<new SHA after edit, or 'pending-commit'>"
     - reject         → update the finding's "Status at HEAD" to:
         "Status at HEAD: rejected — <user's reasoning>"
     - flag           → leave "Status at HEAD: open" but append annotation:
         "Status at HEAD: open — flagged: <user's note>"
     - defer          → update to:
         "Status at HEAD: deferred — <ticket/branch ref or note>"

  5. RECORD the decision in-place in the slice file.
     Never delete or rewrite the reviewer's original Description/Suggested fix.
     Only the "Status at HEAD" line gets updated, plus an optional one-line
     "Apply note" appended below it.
```

### Step 5: Forbidden Responses

Same as `superpowers:receiving-code-review`:
- No "Great feedback!" or "Excellent points!"
- No "You're absolutely right!"
- No implementing before verifying the issue still exists in the code
- No accepting all findings without per-item evaluation
- No thanking the reviewer

### Step 6: Mark Completed

When every finding in the slice has a non-`open` `Status at HEAD`:
- Move file to `completed/`:
  ```bash
  mv planning/code-reviews/needs-review/reviewed/<file>.md planning/code-reviews/needs-review/completed/<file>.md
  ```
- Update frontmatter:
  ```yaml
  status: completed
  status_updated: <ISO 8601 now>
  status_note: ""
  ```

If some findings remain `open` (flagged or deferred), leave the file in `reviewed/` with `status: triaging` so you can come back to it. Update `status_note` to summarize what's left (e.g., "2 flagged findings awaiting design decision on intervention handler").

### Step 7: Notify

After processing each slice (or at end of batch), summarize to chat:
- Slice ID + name
- Counts: accepted, merged, rejected, flagged, deferred
- File location
- If batch: re-run `code-synthesize-reviews` once finished — open findings have shifted, fresh synthesis tells you what's actually left.

## Verifying Edits

For accepted/merged findings, when applying:

- Read the file at the cited line BEFORE editing — confirm context.
- Make the smallest edit that resolves the finding.
- For C# files: respect `CODING-STANDARDS.md` (StyleCop, CRLF, etc.).
- For tests / behaviour-changing edits: run the relevant test suite if it's quick.
- If the edit is risky or non-obvious, pause and discuss with the user before committing.

The slice's `head_sha` is the snapshot the reviewer reviewed against. Your edits land at HEAD-of-branch (whatever's checked out now). When recording `resolved-at-<SHA>`, use the SHA AFTER your fix is committed — or `pending-commit` if you haven't committed yet.

## Do Not

- Auto-accept Critical findings without per-item user confirmation.
- Mass-apply findings via batch script — each finding is a judgment call.
- Rewrite the reviewer's text. The slice spec is the audit trail.
- Move a slice to `completed/` with open findings still present.
- Skip the verification step (Step 4.2) — Status at HEAD can be stale.
- Treat `flag` as `reject` — flagged findings stay visible in synthesis.

## Red Flags

| Thought | Reality |
|---|---|
| "Reviewer is obviously right, just apply" | Verify the issue exists in the current code first. |
| "All these Minor findings can be batch-rejected" | Each one is a judgment call. Walk them. |
| "I'll mark completed even though one is flagged" | No — flagged findings keep the slice in `triaging`. |
| "I can edit the reviewer's description to clarify" | No. Append a note; never modify the original. |
| "I don't have time to verify each one" | Then this isn't the right time to apply. Stop. |
