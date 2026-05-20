---
name: code-synthesize-reviews
description: "Use when multiple code-review slices have been reviewed and you need a consolidated pre-merge action list — reads all slice specs in reviewed/ and completed/, filters findings by Status at HEAD: open, groups by severity and file, emits planning/code-reviews/SYNTHESIS-<slug>.md"
metadata:
  category: review-helpers
  order: 33
---
# Synthesize Code Reviews

Aggregate findings from all reviewed slice specs into a per-review action list at `planning/code-reviews/SYNTHESIS-<slug>.md`.

**Core principle:** Only `Status at HEAD: open` findings need action. Resolved/rejected/partially-addressed findings stay in their slice files as audit trail; the synthesis is the **actionable** view. Each meta-plan gets its own synthesis file so historical reviews don't get clobbered.

## Process

### Step 1: Locate the Meta-Plan and Derive the Slug

Slice specs carry a `parent_plan` frontmatter field pointing at the meta-plan. Read it from any slice spec in the candidate set:

```bash
ls planning/code-reviews/needs-review/reviewed/*.md 2>/dev/null
ls planning/code-reviews/needs-review/completed/*.md 2>/dev/null
```

Read the `parent_plan` from the first slice; open that meta-plan file; extract the `slug` from its frontmatter.

- **Single meta-plan in the candidate set:** use its slug.
- **Multiple distinct meta-plans:** group slices by `parent_plan` and ask the user which review to synthesize. One synthesis per meta-plan run.
- **No `parent_plan` field (legacy slice):** fall back to the user-supplied slug or the literal `legacy`.

The slug becomes part of the output filename: `planning/code-reviews/SYNTHESIS-<slug>.md`.

### Step 2: Collect Slice Specs

```bash
ls planning/code-reviews/needs-review/reviewed/*.md 2>/dev/null
ls planning/code-reviews/needs-review/completed/*.md 2>/dev/null
```

Filter to slices whose `parent_plan` matches the chosen meta-plan. Skip files with `status: failed` (no findings to synthesize); include them in the failed-slices section instead.

If no eligible slices: emit `SYNTHESIS-<slug>.md` with a "no slices reviewed yet" header and stop.

### Step 3: Parse Findings

For each slice spec's `## Code Review` section, parse each finding:
- `ID` (e.g. `F03.2`)
- `Severity` (Critical / Important / Minor)
- `File:line`
- `Status at HEAD` (open / resolved-at-* / rejected — / partially-addressed-at-*)
- `Description`
- `Suggested fix`

Filter to `Status at HEAD: open` AND `Status at HEAD: partially-addressed-at-*`. Include partially-addressed because the unfinished portion still needs action.

### Step 4: Group and Sort

Primary group: severity (Critical → Important → Minor).
Secondary group: file path.
Tertiary sort within file: line number.

### Step 5: Cross-Slice Pattern Detection

Scan for findings across multiple slices that touch the same file or describe the same anti-pattern. Surface these as "cross-slice clusters" at the top of each severity tier — these are usually batch-fixable and worth flagging.

Heuristic for clustering: same file across ≥2 slices, OR same keyword pattern in description (e.g. "missing null check", "no test coverage").

### Step 6: Emit `SYNTHESIS-<slug>.md`

Write `planning/code-reviews/SYNTHESIS-<slug>.md`:

```markdown
# Code Review Synthesis — <branch> (<slug>)

**Generated:** <ISO timestamp>
**Meta-plan:** `<path to meta-plan>`
**Slices included:** <list of slice numbers and names>
**Slices skipped (failed):** <list — or "none">
**Total open findings:** <N>

---

## Critical (Must Address Before Merge)

### Cross-slice clusters
<if any: bullet list with finding IDs and shared theme>

### Per-file
#### path/to/file.cs
- **F01.3** — line 47 — <one-line description> — *fix:* <suggested fix>
- **F03.1** — line 122 — ...

#### path/to/other.ts
- ...

---

## Important (Should Address)
<same shape>

---

## Minor (Consider)
<same shape>

---

## Slice-by-slice index

| Slice | File | Verdict | Open findings (C/I/M) |
|---|---|---|---|
| 01 | YYYY-MM-DD-code-review-slice-01-foo.md | With fixes | 1 / 3 / 2 |
| 02 | ... | Ready | 0 / 0 / 1 |

---

## Failed slices (require re-run)

| Slice | File | Reason |
|---|---|---|
| 03 | ... | reviewer timeout |

---

## Next steps

1. Run `code-review-apply` to triage and apply findings.
2. Re-run `code-intake-review` on any failed slices in `needs-review/in-progress/` once the underlying issue is resolved.
3. Re-run `code-synthesize-reviews` after `code-review-apply` finishes — `Status at HEAD` annotations will have shifted, and a fresh synthesis (overwriting the same `SYNTHESIS-<slug>.md`) shows what truly remains.
```

### Step 7: Notify

Chat output:
- Path to `SYNTHESIS-<slug>.md`
- Total open findings by severity (e.g., "3 Critical, 11 Important, 8 Minor")
- Count of failed slices, if any
- Suggestion: "Run `code-review-apply` next."

## Filename Convention

- **Per-meta-plan:** every code-review run gets its own `SYNTHESIS-<slug>.md`.
- **Same-run reruns:** overwrite the existing file (same `slug` + new content).
- **Slug source:** `slug` field in the meta-plan's frontmatter (set by `code-review-plan-create`).
- **Legacy migration:** if you find a bare `SYNTHESIS.md` from before this rename, leave it alone — it's a historical artifact. New runs always use the slug variant.

## Idempotency

This skill is **read-only on slice specs** — it never writes back to them. Safe to re-run any time. Each run **overwrites** `SYNTHESIS-<slug>.md` with the latest snapshot for that meta-plan; other slugs' synthesis files are untouched.

## Do Not

- Modify slice spec files (read-only).
- Delete or archive `SYNTHESIS-<slug>.md` files — overwriting is fine; deleting loses the audit trail of what was last reported for that review.
- Filter out Minor findings (the user decides what to ignore at apply-time, not synthesis-time).
- Re-classify severities (the reviewer's call stands).
- Write to the bare `SYNTHESIS.md` path — always include the slug.

