---
name: curriculum-govern
description: "Use when validating curriculum before generation or publication — schema, source fidelity, provenance, contradictions, sensitive data, prerequisite integrity, and artifact hashes. Returns approved, approved_with_warnings, or rejected. Runs twice in a generate cycle: once on the course definition, once on the rendered artifacts."
metadata:
  category: curriculum
---

# Curriculum Governance Agent

Contract: [`governance-agent.md`](governance-agent.md). Policy: [`policy.yaml`](policy.yaml).

## Run the script first

```powershell
.\scripts\validate-curriculum.ps1          # workstation
.\scripts\validate-curriculum.ps1 -Strict  # CI: warnings become failures
```

It covers the deterministic gates — `schema_validity`, `source_exists`, `empty_source`, `sources_are_upstream`, `sources_reconciled`, `artifact_hashes_reproduce`, `prerequisite_graph_valid`, `provenance_complete`, `sensitive_data_scan` — in about a second. Spending model tokens on what a regex settles is waste.

## Then do the part the script cannot

**A green validator is not a correct curriculum.** Replayed against a reconstructed pre-fix state, one such script caught **6 of 7** known drifted courses. The miss is the instructive one: a course pointed at a documentation path that existed and held real Markdown — just the *wrong* Markdown (install guides rather than the engineering content it claimed). No file-system gate can catch a source that is present but semantically wrong.

That failure class is yours:

| Gate | What you are actually checking |
|---|---|
| `claims_supported` | Open the cited source. Does it say what the course claims? This is the DATA-201 class. |
| `proposals_labeled` | Is invented instructional content marked `proposed_instructional`, or laundered as source-derived? |
| `contradictions_resolved_or_disclosed` | Two sources disagreeing, presented as one settled fact |
| `deprecated_sources_handled` | Teaching from `planning/not-implemented/`, or a `[REASONED]` claim presented as `[VERIFIED]` |
| `assessment_answers_supported` | Every answer key traceable to a source passage |
| `artifact_hashes_present` | Generator version and source revision on every artifact. **The digests themselves are no longer your job** — `artifact_hashes_reproduce` in the script recomputes and compares every one of them. What is left for you: is anything *missing* from the manifest that should be in it, and does each artifact's recorded provenance name a real source. Record new digests with [`hash-artifact.ps1`](hash-artifact.ps1) (`-Normalize` on a CRLF checkout); see [`artifact-generation-agent.md`](../curriculum-generate/artifact-generation-agent.md#content-hashing). |
| `publication_target_allowed` | Currently **no approved destination exists** — publication is blocked by policy |

## Guardrails

- **Do not rewrite content to make it pass.** Report and stop.
- **Do not approve an exception you proposed.** If you suggested the workaround, you are not the one who signs it off.
- **Do not downgrade a failure because publication was requested.** Deadline pressure is the exact condition this gate exists for.
- Fail closed if the policy profile will not load.

Return `approved`, `approved_with_warnings`, or `rejected`, with exact finding locations and required actions.
