---
name: plan-intake-review
description: Use on a loop interval to check for plans awaiting review in planning/needs-review/, move to in-progress, dispatch plan-reviewer subagent, append feedback, and move to reviewed/ - silent when no files found
metadata:
  category: review-helpers
  order: 11
---
# Plan Review Intake

Poll the review queue, pick up plans, review them with a subagent, and deposit reviewed plans with feedback appended.

**Core principle:** Move before processing. The file system state reflects reality at all times.

## Process

### Step 1: Check for Work

```bash
ls planning/needs-review/*.md 2>/dev/null | grep -v .gitkeep
```

- **No files:** Do nothing. Return silently. No output at all. (This skill runs on a loop.)
- **One file:** Proceed to Step 2.
- **Multiple files:** Ask the user which one to review. Wait for answer.

### Step 2: Check for Stale In-Progress

```bash
ls planning/needs-review/in-progress/*.md 2>/dev/null | grep -v .gitkeep
```

If files exist in `in-progress/`:
- Alert the user: "Found stale in-progress review: `<filename>`. A previous review did not complete."
- Ask: Resume the stale review, or move it back to `needs-review/` and start fresh?
- Wait for user decision before proceeding.

### Step 2.5: Defensive Kind Check

Read the file's frontmatter `kind:` field. A review copy is `kind: review`. Its `type:`
(`plan` or `code-review-plan`) only says what kind of plan it was copied from:

| Found | Action |
|---|---|
| `kind: review` | OK — proceed to Step 3. A meta-plan (`type: code-review-plan`) reviews like any plan. |
| `kind: plan`, or no `kind:` | OK — an older copy. Proceed to Step 3, and when it reaches `reviewed/`, tell the author to give it the `kind: review` block (`plan-send-review` Step 3). |
| `kind: slice`, or legacy `type: code-review-slice` | **Auto-forward.** Move the file to `planning/code-reviews/needs-review/<file>.md` and alert: "Auto-forwarded `<filename>` from the plan queue to the code-review queue." Skip this run, because `code-intake-review` will pick it up. |
| `kind: reminder` | Leave it in `needs-review/` with an intake-error note: reminders are not reviewed (see `reminders-set`). Alert the user. |
| any other value | Leave it in `needs-review/` with a one-line intake-error note prepended quoting the unexpected value. Alert the user. |

This is the mirror of `code-intake-review`'s Step 3a. Together the two checks self-correct a slice dropped in the wrong queue without losing the file — which matters because the two queues are one keystroke apart and the failure is otherwise silent.

**On the `reviews:` back-reference (and any other reference) — resolve by the name rule, never by eye.** A review copy carries `reviews: <plan name>` (`plan-send-review` Step 3), and the plan it was copied from may carry `parent_plan:`, `delivers:`, or body links. Every one of these is a **name**, not a filename:

> A name is the document's filename with its kind prefix **and** `.md` removed. Only `feature_`, `bug_` and `stub_` are stripped — `wave-<n>_` is **not**. So `reviews: pii-ner-processor-service_2026-09-25` and the file `feature_pii-ner-processor-service_2026-09-25.md` **are a match** — the absent `feature_` is the name working as designed, not a mismatch to flag.

Do **not** reject a review copy because a reference "doesn't exactly match" a filename. The rule above is portable and needs no tooling — apply it directly. Where you can run C#, `plan-doc.cs validate --path planning` is the authority (it resolves `reviews:` / `parent_plan` / `delivers:`); invoke it as `plan-workflow` → *The scripts* documents, since that skill owns the script and the one correct way to locate it. Do not re-derive the script path here, and do not assume the validator is reachable — it is not in every harness (notably not vanilla Copilot CLI), which is why the rule is stated inline above.

A value containing `/`, `\`, or a trailing `.md` is a path, which **is** an error (`PATH-NOT-NAME`); a bare name that resolves under the rule is not. The full contract lives in `plan-writing-syntax` ("References are names, never paths"). `code-intake-review` Step 3 states the same rule for a slice's `plan_docs`.

### Step 3: Move to In-Progress

```bash
mv planning/needs-review/<file>.md planning/needs-review/in-progress/<file>.md
```

**This move MUST happen BEFORE any review processing begins.** This prevents a second agent from picking up the same file.

The frontmatter stays `kind: review`, `status: open` through `in-progress/` and `reviewed/`; the
validator accepts an open review in all three queue folders.

### Step 4: Dispatch Plan Reviewer

Dispatch a `superpowers:code-reviewer` subagent using the template at `plan-intake-review/plan-reviewer.md`.

Fill these placeholders:
- `{PLAN_FILE_PATH}` — Absolute path to the file in `in-progress/`
- `{PROJECT_ROOT}` — Project root directory
- `{PLAN_TITLE}` — The H1 heading from the plan file

The subagent reviews the plan for: architecture soundness, completeness, implementability, consistency with CLAUDE.md conventions, and gaps.

### Step 5: Append Review to Plan

Take the subagent's output and append it to the plan file in `in-progress/`:

```markdown

---

## Plan Review

**Reviewed:** YYYY-MM-DD HH:MM
**Reviewer:** Claude Code (plan-review-intake)

### Strengths
[From subagent output]

### Issues

#### Critical (Must Address Before Implementation)
[From subagent output]

#### Important (Should Address)
[From subagent output]

#### Minor (Consider)
[From subagent output]

### Recommendations
[From subagent output]

### Assessment
**Implementable as written?** [Yes/No/With fixes]
**Reasoning:** [1-2 sentences]
```

### Step 6: Move to Reviewed

```bash
mv planning/needs-review/in-progress/<file>.md planning/needs-review/reviewed/<file>.md
```

### Step 7: Notify

If running interactively (not on a `/loop`), report:
- Which plan was reviewed
- Summary of assessment (implementable? critical issues?)
- Where the reviewed file is

If running on a loop, still output a brief one-liner so the user knows it ran:
`Plan reviewed: <filename> — <assessment verdict>`

## Error Handling

| Situation | Action |
|-----------|--------|
| File disappeared from in-progress | Another agent moved it. Log warning, skip. |
| Subagent fails | Move file back to `needs-review/`. Report error to user. |
| Stale file in in-progress | Ask user before proceeding (Step 2). |
| Plan file has no content | Move to reviewed with note: "Empty plan — nothing to review." |
