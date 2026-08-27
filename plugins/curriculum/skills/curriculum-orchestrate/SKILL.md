---
name: curriculum-orchestrate
description: Use when generating or refreshing course curriculum from source repositories — turning documentation into course definitions and course artifacts. Sequences discovery, classification, learning extraction, curriculum design, governance, and artifact generation, and stops at a failed gate. Triggers on "regenerate the course", "build curriculum from", "what courses does this change affect", or after a documentation change that a course sources from.
metadata:
  category: curriculum
---

# Curriculum Orchestrator

Coordinates the curriculum pipeline without owning any specialist logic. The contract is [`curriculum-orchestrator.md`](curriculum-orchestrator.md) — read it; this skill is how to run it in your harness, not a restatement of it.

> **These skills only work inside a curriculum workspace.** They read a `learning/` tree and the source repos named in your `sources.yaml` (start from [`sources.example.yaml`](../curriculum-discover/sources.example.yaml)), so every `learning/...` path in this family resolves against that workspace, not against the skill folder. Run them anywhere else and they have nothing to operate on.

## Before anything else

**`learning/` is downstream.** Courses are outputs of this pipeline, never inputs to it. If you find yourself reading a course to write a course, stop — the source is in `documentation/` or another repo.

## Operations

| Operation | Runs |
|---|---|
| `discover` | curriculum-discover |
| `preview` | discover → classify → extract → design → govern |
| `generate` | preview → generate-artifacts → govern (again, against the artifacts) |
| `publish` | govern → **blocked** unless your policy names an approved destination |

Governance runs **twice** in `generate` — once on the course definition, once on what was rendered from it. Skipping the second is how an approved course produces an unapproved artifact.

## Running it

1. **Establish the source revision first.** Record the commit of every repo you are reading. A run against a dirty tree cannot be reproduced and must not be published.

2. **Determine impacted courses.** For a documentation change, find which courses cite it:

   ```bash
   grep -rl "path: <changed-path>" learning/courses/*/course.yaml
   ```

   Courses in other repos cite it too — check every repo listed in your `sources.yaml`.

3. **Run the deterministic gates before spending tokens on agents:**

   ```powershell
   .\scripts\validate-curriculum.ps1
   ```

   A broken source path fails here in a second. Discovering it after four agent stages is waste.

4. **Invoke specialists in dependency order.** Pass structured outputs, not conversational summaries. Preserve `correlation_id`, `source_revision`, evidence, warnings and errors through every step — the envelope is in the contract.

5. **Stop at a failed gate.** Do not repair content to make it pass, and do not downgrade a failure because generation was requested.

## Guardrails

These are the ones most likely to be rationalized away mid-run:

- **Never publish from an unresolved source revision.** Fail closed.
- **Never treat a generated instructional example as a documented standard.** If the source did not say it, the course may not assert it.
- **Never invent a missing procedure.** A documentation gap is a bug to file against `documentation/`, not a hole for the course to paper over. A course that fills the gap locally lies the moment the real procedure lands.
- **Never edit source documentation as a side effect.** If the source is wrong, that is a separate change with its own review.
- **Never let a specialist publish directly.**

## Partial failure

Continue with partial discovery when individual files are unreadable, and record every omission — a silently shortened source manifest produces a confidently incomplete course. Do not retry permanent validation failures; bounded retries for transient tool or network failures only.

## Output

A run manifest: outputs, warnings, errors, provenance, and publication status. Every artifact carries a content hash, generator version, and source revision — that is what makes reprocessing unchanged content idempotent, and what lets the sync agent tell changed from unchanged without re-uploading everything.
