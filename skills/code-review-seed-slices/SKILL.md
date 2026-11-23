---
name: code-review-seed-slices
description: Use when a code-review meta-plan is finalized and you need to fan it out into one slice per row in the meta-plan's slice table — produces N kind slice files in planning/code-reviews/needs-review/ ready for code-intake-review to drain
metadata:
  category: review-helpers
  order: 31
---

# Seed Code-Review Slices from a Meta-Plan

Read a meta-plan (`kind: plan`, `type: code-review-plan`) and write one slice (`kind: slice`) to `planning/code-reviews/needs-review/` for each row in the meta-plan's slice table.

**Core principle:** This skill is the loop body that turns a meta-plan into a queue. It is normally called by `code-review-plan-execute`; the user can also call it directly after writing a meta-plan if they want to seed without running the full execute pipeline yet.

## Process

### Step 1: Load the Meta-Plan

The meta-plan is supplied as input, usually by `code-review-plan-execute`. If it is not, ask the user.

Validate:
- File exists.
- Frontmatter `kind: plan` and `type: code-review-plan`.
- Frontmatter has `initiative`, `branch`, `base_sha`, `head_sha`, `slice_count`. An older meta-plan
  carries `slug:` instead of `initiative:`. Read it as the
  initiative, and tell the user to rename the field.
- Body contains a slice table (markdown table or YAML list — see Step 2).

Work out the meta-plan's **name**: the filename minus `.md` (for example
`code-review-feature-state-machine_2026-11-04`). Every slice records this as `parent_plan:`.

### Step 2: Parse the Slice Table

Each row in the meta-plan's slice table contributes one slice. Required columns/fields per row:

| Field | Source |
|---|---|
| slice number | row position (1-indexed, zero-padded to NN) |
| title | row's "name" / "slug" cell |
| base_sha | row's "base" cell |
| head_sha | row's "head" cell |
| plan_docs | row's "plan_docs" cell: plan names, comma- or list-separated |
| focus_areas | row's "focus" cell (free text or bullet list) |

If any required field is missing on any row, abort with a clear error citing the row.

### Step 3: Pre-flight

For each parsed slice row:
- `git rev-parse <base_sha>` and `git rev-parse <head_sha>` succeed.
- `git log --oneline <base_sha>..<head_sha>` returns ≥1 commit.
- Each `plan_docs` entry is a name (no `/`, no `.md`) that matches exactly one plan or stub under
  `planning/`. A path in the meta-plan's table is converted to its name here. Plans outside the
  repository move into the slice body (see `code-send-review`).
- No slice for this **initiative and slice number** already exists across `needs-review/`,
  `in-progress/`, `reviewed/`, `completed/` (collision check, scoped to the initiative):

  ```bash
  ls planning/code-reviews/needs-review/{,in-progress/,reviewed/,completed/}*-code-review-<initiative>-slice-<NN>-*.md 2>/dev/null
  ```

If any row fails pre-flight, abort with a per-row report. Do not partially seed.

### Step 4: Write Slices

For each row, follow `code-send-review`'s template and write the slice at
`planning/code-reviews/needs-review/YYYY-MM-DD-code-review-<initiative>-slice-NN-<title>.md`. Its
frontmatter:

```yaml
---
kind: slice
status: queued
date: <YYYY-MM-DD>
initiative: <from meta-plan>
parent_plan: <meta-plan name>
slice: <NN>
title: <from row>
branch: <from meta-plan>
base_sha: <from row>
head_sha: <from row>
diff_stat: "<computed via git diff --stat>"
plan_docs:
  - <plan name>
status_updated: <ISO 8601 now>
status_note: ""
---
```

The body of each slice includes the row's focus areas verbatim and the `git log --oneline base..head` for that range.

### Step 5: Confirm

Report to caller (chat output):
- Meta-plan name
- Number of slices seeded
- Per-slice: filename, slice number, commit count, plan_docs count

## Failure Mode

If pre-flight passes but a write fails partway through (e.g., disk error after writing 3 of 8 slices):
- Report which slices succeeded.
- Do NOT delete partial writes — let the user inspect and decide.
- Status of failed seeding is reported to caller; `code-review-plan-execute` should NOT proceed to the review loop until seeding is complete.

## Do Not

- Seed slices that don't pass pre-flight — partial state is the enemy.
- Mutate the meta-plan during seeding (read-only).
- Move the meta-plan. Board moves belong to `code-review-plan-execute` and `code-review-apply`.
- Re-seed slices that already exist anywhere in the queue (collision check catches this).
- Write the legacy `*-code-review-slice-NN-*` filename, `type: code-review-slice`, or a path in
  `parent_plan` / `plan_docs`.
