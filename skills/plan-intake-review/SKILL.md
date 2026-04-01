---
name: plan-intake-review
description: Use on a loop interval to check for plans awaiting review in planning/needs-review/, move to in-progress, dispatch plan-reviewer subagent, append feedback, and move to reviewed/ - silent when no files found
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

### Step 3: Move to In-Progress

```bash
mv planning/needs-review/<file>.md planning/needs-review/in-progress/<file>.md
```

**This move MUST happen BEFORE any review processing begins.** This prevents a second agent from picking up the same file.

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
