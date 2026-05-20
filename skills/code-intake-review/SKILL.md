---
name: code-intake-review
description: Use on a loop interval to check for code slices awaiting review in planning/code-reviews/needs-review/, move to in-progress, dispatch code-reviewer subagent, append findings, and move to reviewed/ - silent when no files found
metadata:
  category: review-helpers
  order: 21
---
# Code Review Intake

Poll the code-review queue, pick up slice specs, review the diff with a subagent, append findings in-place, and deposit the spec in `reviewed/`.

**Core principle:** Move before processing. The file system state reflects reality at all times.

## Process

### Step 1: Check for Work

Slice spec filenames follow either of two conventions:

| Form | Pattern | Example |
|---|---|---|
| Current (preferred) | `YYYY-MM-DD-code-review-<initiative>-slice-NN-<slug>.md` | `2026-04-27-code-review-chat-history-3-0-slice-01-preflight-skeleton.md` |
| Legacy (still honored) | `YYYY-MM-DD-code-review-slice-NN-<slug>.md` | `2026-04-25-code-review-slice-01-state-machine-foundation.md` |

Files that do not match either pattern are ignored.

```bash
ls planning/code-reviews/needs-review/*-code-review-*-slice-*.md \
   planning/code-reviews/needs-review/*-code-review-slice-*.md 2>/dev/null \
   | grep -v .gitkeep | sort -u
```

The first glob catches the current form; the second catches legacy. `sort -u` collapses any duplicates (legacy files happen to also match the current glob since `<initiative>` between `code-review-` and `-slice-` can be any token).

- **No files:** Do nothing. Return silently. No output at all. (This skill runs on a loop.)
- **One file:** Proceed to Step 2.
- **Multiple files:** Ask the user which slice to review. Wait for answer.

If files exist in the directory that do NOT match the convention, alert the user once at the start of the run — do not silently ignore them, but do not block on them either.

### Step 2: Check for Stale In-Progress

```bash
ls planning/code-reviews/needs-review/in-progress/*.md 2>/dev/null | grep -v .gitkeep
```

If files exist in `in-progress/`:
- Alert the user: "Found stale in-progress review: `<filename>`. A previous review did not complete."
- Ask: Resume the stale review, or move it back to `needs-review/` and start fresh?
- Wait for user decision before proceeding.

### Step 3: Validate the Slice Spec

Read the file. Confirm:
- Frontmatter `type:` field equals `code-review-slice` (REQUIRED — see Step 3a below for mismatch handling).
- Frontmatter includes `slice`, `base_sha`, `head_sha`, `plan_docs` (possibly empty).
- Frontmatter `initiative:` field is present for files using the current naming form (`*-code-review-<initiative>-slice-NN-*.md`). Legacy form files (`*-code-review-slice-NN-*.md`) may omit it.
- `git rev-parse <base_sha>` and `git rev-parse <head_sha>` both succeed.
- Each `plan_docs` path exists on disk.
- `parent_plan` (if present) exists on disk.

If any check fails (other than type mismatch — see 3a), move the file back to `needs-review/` with a note prepended (`## Intake Error — <reason>`) and alert the user. Do not dispatch a subagent on a broken spec.

**Filename ↔ frontmatter consistency.** If the filename is current-form (`*-code-review-<X>-slice-NN-*.md`) but `initiative:` in the frontmatter is missing, OR if the filename's `<X>` segment doesn't match the frontmatter `initiative:` value, that's an intake error. The author got the convention wrong; flag it back to `needs-review/` with a note rather than reviewing under an incorrect grouping.

### Step 3a: Defensive Type Check (Auto-Forward on Mismatch)

If `type:` is missing or set to one of the **plan-stream** types, this file does not belong here. Auto-forward to the correct queue and skip:

| Found type | Action |
|---|---|
| `code-review-slice` | OK — proceed to Step 4. |
| `plan` | Move file to `planning/needs-review/` and alert: "Auto-forwarded plan-typed file from code-review queue to plan queue: `<filename>`." Do not dispatch. |
| `code-review-plan` | Same — move to `planning/needs-review/`. The plan-* trio handles meta-plans like any other plan. |
| (missing) | Move file back to `needs-review/` (root) with intake-error note: "Slice spec missing `type: code-review-slice` frontmatter." Alert user. |
| any other value | Same as missing — move back with intake-error note quoting the unexpected value. |

The plan-* intake skill performs the symmetric check; together they self-correct user mistakes without losing files.

### Step 4: Move to In-Progress and Update Status

```bash
mv planning/code-reviews/needs-review/<file>.md planning/code-reviews/needs-review/in-progress/<file>.md
```

Then edit the file's frontmatter to set:
```yaml
status: reviewing
status_updated: <ISO 8601 now>
status_note: ""
```

**The move + status update MUST both happen BEFORE any review processing begins.** Prevents a second agent from picking up the same file. The `status: reviewing` is what `code-review-plan-execute` uses to detect stale runs (file in `in-progress/` with `status: reviewing` and old `status_updated` = crashed previous run).

### Step 5: Dispatch Code Reviewer

Dispatch a `superpowers:code-reviewer` subagent using the template at `code-intake-review/code-reviewer.md`.

Fill these placeholders:
- `{SLICE_FILE_PATH}` — Absolute path to the file in `in-progress/`
- `{PROJECT_ROOT}` — Project root directory
- `{SLICE_TITLE}` — The H1 heading from the slice file
- `{BASE_SHA}` — from the frontmatter
- `{HEAD_SHA}` — from the frontmatter
- `{PLAN_DOCS}` — newline-joined list from frontmatter (or "None")

The subagent reviews the `git diff <base_sha>..<head_sha>` against the focus areas, plan docs, and `CLAUDE.md` conventions. It produces findings with a **Status at HEAD** field per finding.

### Step 6: Append Review to Slice Spec

Take the subagent's output and append it to the slice file in `in-progress/`:

```markdown

---

## Code Review

**Reviewed:** YYYY-MM-DD HH:MM
**Reviewer:** Claude Code (code-intake-review)
**Diff reviewed:** <base_sha>..<head_sha>

### Strengths
[From subagent output]

### Findings

#### Critical (Must Address Before Merge)
[From subagent output — each finding uses the shape below]

#### Important (Should Address)
[From subagent output]

#### Minor (Consider)
[From subagent output]

### Finding shape

For every finding the subagent emits:

- **ID:** `F<slice>.<n>` (e.g. `F01.3`)
- **File:** `path/to/file.ext:line`
- **Severity:** Critical | Important | Minor
- **Introduced:** `<sha>` (commit within this slice that introduced it)
- **Status at HEAD:** `open` | `resolved-at-<sha>` | `partially-addressed-at-<sha>`
- **Description:** What's wrong
- **Suggested fix:** Concrete change

### Assessment

**Ready to merge?** [Yes/No/With fixes]

**Reasoning:** [1-2 sentences]
```

### Step 7: Move to Reviewed and Update Status

On reviewer success:

```bash
mv planning/code-reviews/needs-review/in-progress/<file>.md planning/code-reviews/needs-review/reviewed/<file>.md
```

Update frontmatter:
```yaml
status: reviewed
status_updated: <ISO 8601 now>
status_note: ""
```

On reviewer failure (subagent error, timeout, validation error in output):

Leave the file in `in-progress/`. Update frontmatter:
```yaml
status: failed
status_updated: <ISO 8601 now>
status_note: "<short error reason — e.g. 'reviewer timeout after 600s' or 'subagent returned no findings'>"
```

The `failed` status is what `code-review-plan-execute` reads to skip the slice and surface it in the end-of-run summary, instead of silently retrying or aborting the whole batch.

### Step 8: Notify

If running interactively (not on a `/loop`), report:
- Which slice was reviewed
- Summary of assessment (ready to merge? counts by severity)
- Where the reviewed file is

If running on a loop, still output a brief one-liner:
`Slice reviewed: slice-NN-<name> — <assessment> (C/I/M: a/b/c)`

## Error Handling

| Situation | Action |
|-----------|--------|
| File disappeared from in-progress | Another agent moved it. Log warning, skip. |
| Subagent fails | Move file back to `needs-review/`. Report error. |
| Stale file in in-progress | Ask user before proceeding (Step 2). |
| SHAs don't rev-parse | Move back to `needs-review/` with intake-error note; alert user. |
| Plan-doc path missing | Same as SHA failure. |
| Slice spec has no commits | Move to reviewed with note: "Empty slice — nothing to review." |

## Do Not

- Skip the in-progress move.
- Let a subagent write findings without the Status-at-HEAD field — if it's missing, ask the subagent to re-emit.
- Modify the slice spec's author-written sections — append only.
- Dispatch on a broken spec (missing SHAs, missing plan docs).
