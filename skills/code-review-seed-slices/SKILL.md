---
name: code-review-seed-slices
description: Use when a code-review meta-plan is finalized and you need to fan it out into one slice spec per row in the meta-plan's slice table — produces N files in planning/code-reviews/needs-review/ ready for code-intake-review to drain
metadata:
  category: review-helpers
  order: 31
---
# Seed Code-Review Slice Specs from a Meta-Plan

Read a meta-plan (`type: code-review-plan`) and write one slice spec to `planning/code-reviews/needs-review/` for each row in the meta-plan's slice table.

**Core principle:** This skill is the loop body that turns a meta-plan into a queue. It is normally called by `code-review-plan-execute`; the user can also call it directly after writing a meta-plan if they want to seed without running the full execute pipeline yet.

## Process

### Step 1: Load the Meta-Plan

Path is supplied as input (typically by `code-review-plan-execute`). If not supplied, ask the user.

Validate:
- File exists.
- Frontmatter `type: code-review-plan`.
- Frontmatter has `branch`, `base_sha`, `head_sha`, `slug`, `slice_count`.
- Body contains a slice table (markdown table or YAML list — see Step 2).

### Step 2: Parse the Slice Table

Each row in the meta-plan's slice table contributes one slice spec. Required columns/fields per row:

| Field | Source |
|---|---|
| slice number | row position (1-indexed, zero-padded to NN) |
| name | row's "name" / "slug" cell |
| base_sha | row's "base" cell |
| head_sha | row's "head" cell |
| plan_docs | row's "plan_docs" cell (comma- or list-separated) |
| focus_areas | row's "focus" cell (free text or bullet list) |

If any required field is missing on any row, abort with a clear error citing the row.

### Step 3: Pre-flight

For each parsed slice row:
- `git rev-parse <base_sha>` and `git rev-parse <head_sha>` succeed.
- `git log --oneline <base_sha>..<head_sha>` returns ≥1 commit.
- Each `plan_docs` path exists on disk.
- No file already exists for this slice number across `needs-review/`, `in-progress/`, `reviewed/`, `completed/` (collision check).

If any row fails pre-flight, abort with a per-row report. Do not partially seed.

### Step 4: Write Slice Specs

For each row, invoke `code-send-review`'s logic to write the slice spec at `planning/code-reviews/needs-review/YYYY-MM-DD-code-review-slice-NN-<slug>.md` with frontmatter:

```yaml
---
type: code-review-slice
slice: <NN>
name: <slug>
branch: <from meta-plan>
base_sha: <from row>
head_sha: <from row>
diff_stat: "<computed via git diff --stat>"
plan_docs:
  - <path1>
  - <path2>
parent_plan: <absolute path to the meta-plan>
status: queued
status_updated: <ISO 8601 now>
status_note: ""
created: <YYYY-MM-DD>
---
```

Body of each slice spec includes the row's focus areas verbatim and the `git log --oneline base..head` for that range.

### Step 5: Confirm

Report to caller (chat output):
- Meta-plan path
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
- Move the meta-plan after seeding — it stays where it lives (`planning/needs-review/completed/` or `planning/`).
- Re-seed slices that already exist anywhere in the queue (collision check catches this).
