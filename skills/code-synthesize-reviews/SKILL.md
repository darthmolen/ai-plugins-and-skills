---
name: code-synthesize-reviews
description: Use when multiple code-review slices have been reviewed and you need a consolidated pre-merge action list — reads one initiative's slices, filters findings by Status at HEAD open, groups by severity and file, and writes a new append-only synthesis at planning/code-reviews/{date}-code-review-{initiative}-synthesis-{NN}.md that supersedes the previous one
metadata:
  category: review-helpers
  order: 33
---
# Synthesize Code Reviews

Aggregate the open findings from one initiative's reviewed slices into an action list. Each run
writes a **new** synthesis file. The one it replaces is marked superseded and filed beside the
completed slices, so no run's report is ever overwritten.

**Core principle:** Only `Status at HEAD: open` findings need action. Resolved, rejected and
deferred findings stay in their slice files as the audit trail. The synthesis is the
**actionable** view at one moment, and the sequence of syntheses records how that view changed.

## The Synthesis Document

A synthesis is a planning document (`kind: synthesis`, per `plan-writing-syntax`), validated by
`plan-doc.cs validate` like the slices it summarises.

**Filename:** `planning/code-reviews/YYYY-MM-DD-code-review-<initiative>-synthesis-NN.md`

- It follows the slices' naming (`YYYY-MM-DD-code-review-<initiative>-slice-NN-<title>.md`), so
  an initiative's documents sort together.
- `NN` is two digits, sequential **within the initiative**, across every run and every date. The
  next number is one more than the highest existing `*-code-review-<initiative>-synthesis-NN.md`
  anywhere under `planning/code-reviews/`. Two runs on the same day therefore never clash.

**Frontmatter:**

```yaml
---
kind: synthesis
status: open                 # open | done
date: YYYY-MM-DD
initiative: <initiative>
parent_plan: <meta-plan name>
superseded_by: <name>        # only once a later synthesis replaces this one
---
```

**Lifecycle:**

| status | directory | when |
|---|---|---|
| `open` | `planning/code-reviews/` | the current synthesis for the initiative; at most one at a time |
| `done` | `planning/code-reviews/needs-review/completed/` | superseded by a later run, or retired by `code-review-apply` when the initiative closes |

## Process

### Step 1: Pick the Initiative

List the initiatives with reviewed work:

```bash
grep -h "^initiative:" planning/code-reviews/needs-review/reviewed/*-code-review-*-slice-*.md \
                       planning/code-reviews/needs-review/completed/*-code-review-*-slice-*.md 2>/dev/null | sort -u
```

- **One initiative:** use it.
- **Several:** ask the user which one. A synthesis covers exactly one initiative.
- **Legacy slices** (no `kind:`, no `initiative:`): do not synthesize them. Ask the user to migrate
  them first (see `code-send-review`'s legacy collision check).

Read `parent_plan` from the initiative's slices. Every slice in an initiative names the same
meta-plan, and a disagreement is an error to report, not to guess around.

### Step 2: Collect Slices

Take every `kind: slice` file with this `initiative:` from `needs-review/reviewed/` and
`needs-review/completed/`. Match slice filenames only (`*-code-review-<initiative>-slice-*.md`),
so a retired synthesis in `completed/` is never read as a slice.

Slices with `status: failed` have no findings. List them in the failed-slices section instead.

### Step 3: Parse Findings

For each slice's `## Code Review` section, parse each finding:
- `ID` (e.g. `F03.2`)
- `Severity` (Critical / Important / Minor)
- `File:line`
- `Status at HEAD` (open / resolved-at-* / rejected — / deferred — / partially-addressed-at-*)
- `Description`
- `Suggested fix`

Keep `Status at HEAD: open` (including `open — flagged`) and `partially-addressed-at-*`. Include
partially-addressed findings because the unfinished part still needs action.

### Step 4: Group and Sort

Primary group: severity (Critical → Important → Minor).
Secondary group: file path.
Tertiary sort within file: line number.

### Step 5: Cross-Slice Pattern Detection

Scan for findings across multiple slices that touch the same file or describe the same anti-pattern. Surface these as "cross-slice clusters" at the top of each severity tier — these are usually batch-fixable and worth flagging.

Heuristic for clustering: same file across ≥2 slices, OR same keyword pattern in description (e.g. "missing null check", "no test coverage").

### Step 6: Write the New Synthesis

Work out `NN` (see *The Synthesis Document*) and write the new file with `status: open`:

```markdown
---
kind: synthesis
status: open
date: <YYYY-MM-DD>
initiative: <initiative>
parent_plan: <meta-plan name>
---

# Code Review Synthesis NN — <branch> (<initiative>)

**Generated:** <ISO timestamp>
**Supersedes:** <previous synthesis name, or "none — first synthesis">
**Slices included:** <list of slice numbers and titles>
**Slices skipped (failed):** <list — or "none">
**Total open findings:** <N> (previous synthesis: <M>)

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
| 01 | YYYY-MM-DD-code-review-<initiative>-slice-01-foo.md | With fixes | 1 / 3 / 2 |
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
3. Re-run `code-synthesize-reviews` after `code-review-apply` finishes. It writes synthesis NN+1, which shows what truly remains, and files this one as superseded.
```

If no slices are eligible, still write the synthesis, with a "no slices reviewed yet" header.

### Step 7: Supersede the Previous Synthesis

If the initiative already had an `open` synthesis in `planning/code-reviews/`, update its frontmatter
to `status: done` and `superseded_by: <new synthesis name>`. Leave its body untouched. Then move it:

```bash
git mv planning/code-reviews/<previous>.md planning/code-reviews/needs-review/completed/
```

(Use plain `mv` if the previous file was never committed.) Leave exactly one `open` synthesis per
initiative. Run `plan-doc.cs validate --path planning`.

### Step 8: Notify

Chat output:
- Path to the new synthesis, and the name of the one it superseded
- Total open findings by severity (e.g., "3 Critical, 11 Important, 8 Minor"), and the change since the previous synthesis
- Count of failed slices, if any
- Suggestion: "Run `code-review-apply` next."

## Retirement

`code-review-apply` retires the last open synthesis when every slice in the initiative is
`completed`. It sets `status: done`, with no `superseded_by` because nothing replaced it, and moves
the file to `needs-review/completed/` in the same pass that completes the meta-plan. After that
the initiative is closed. A later review of the same branch starts a new meta-plan and a new
initiative.

## Legacy Files

A bare `SYNTHESIS.md` or `SYNTHESIS-<slug>.md` from an older version of this workflow has no frontmatter,
and `plan-doc.cs validate` reports it as `NO-FRONTMATTER`. Migrate it once. Give it the frontmatter
above with `status: done`, set `initiative:` and `parent_plan:` from its slices, and `git mv` it to
`needs-review/completed/`. Keep the filename, because the name is the identity.

## Idempotency

This skill is **read-only on slices** and never writes back to them. Each run adds one file and
retires one. Re-running with no change to the slices produces a new synthesis with the same
findings, which is harmless. Ask before running twice in a row, because the second file adds
nothing.

## Do Not

- Modify slice files (read-only).
- Overwrite or delete any synthesis. Supersede it.
- Leave two `open` syntheses for one initiative.
- Synthesize two initiatives into one file.
- Filter out Minor findings (the user decides what to ignore at apply-time, not synthesis-time).
- Re-classify severities (the reviewer's call stands).
