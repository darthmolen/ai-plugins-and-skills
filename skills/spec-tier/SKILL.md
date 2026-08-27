---
name: spec-tier
description: Route a task to a Low/Medium/High spec tier by stakes, then enforce the matching spec depth, review gates, and calibration. Use when starting any plan or spec, deciding how much detail a plan needs, onboarding developers to the planning standard, or when unsure whether a spec is over- or under-specified. Invoke BEFORE plan-workflow so it sets the depth plan-workflow then produces.
metadata:
  category: workflow-composers
---

# Spec Tiering — match spec depth to stakes; trade spec-rigor against review-rigor

## Thesis

**Total delivery safety ≈ spec rigor + review rigor. You can trade between them.**

Depth is not a virtue by default — it is *ambiguity insurance* whose value scales with the
**cost of being wrong**. Over-specifying trivial, cheaply-verified work wastes effort. Under-specifying
high-stakes work defers ambiguity into the *expensive* phase (implementation), where a wrong turn means
wrong code, tests that encode the wrong contract, and a review with no spec to check against.

Two corollaries drive this skill:

1. **Don't predict iteration count (`n`); assess stakes.** `n` (how many attempts a task takes) is
   non-deterministic and fat-tailed — most tasks converge in 2–3, the bad ones blow up to 8–10 with
   regressions, and you can't tell which upfront. A detailed spec's real job is *collapsing the variance
   of `n`*. So decide by the input you *can* assess upfront (stakes), not the one you can't (`n`).
2. **Brevity ≠ better output.** A thinner spec makes the model *guess more*, and non-determinism makes
   those guesses vary run-to-run. On high-stakes work, brevity trades control for variance. The lever is
   not "less depth" — it is **who pays for the depth**: let AI draft the spec and let a *different* model
   adversarially review it, so the human cost of depth drops while the depth stays.

## Step 1 — Score the stakes (before writing the plan)

Rate each factor Low / Med / High:

| Factor | Low | High |
|---|---|---|
| **Blast radius** | one file, one consumer | many files/projects, cross-cutting, public surface |
| **Cost of being wrong** | trivially reversible | hard-to-reverse: migration, public API, security, customer/data |
| **Verification cost** | compiler / a unit test / obvious repro catches a mistake | failure is emergent, integration-only, or judgment-laden |
| **Executor context** | you (expert, holds the context) | mid-level dev, or an unattended AI agent |
| **Novelty / ambiguity** | well-trodden pattern | green-field, unfamiliar seam, multiple valid designs |

**Routing (worst-factor-dominates):**
- **Any factor at High** — especially *cost-of-being-wrong* or *hard-to-reverse* — → **HIGH**.
- **All factors Low** → **LOW**.
- **Otherwise** → **MEDIUM**.

State the score and the resulting tier at the top of the plan. That one line is the audit trail for why
the plan is as deep (or as light) as it is.

## Step 2 — Produce to the tier

| Tier | Spec depth | Review gates | Executor |
|---|---|---|---|
| **Low** | Problem + the one change + the test that proves it. No cycle list, no review-response table. | Optional. TDD + compiler carry it. | anyone |
| **Medium** | Design summary + locked key decisions + **test-first cycle list** + scope fence + acceptance. Skip the review-response table unless review surfaces findings. | **Adversarial code-review caboose REQUIRED** (cross-harness). Adversarial plan review strongly recommended. | mid-level dev or AI agent |
| **High** | **Full anatomy** (below). | Adversarial **plan** review **and** code-review caboose **and** author's personal review. | executor-agnostic |

**HIGH spec anatomy** (the mechanics live in `plan-workflow` / `writing-plans` — that skill is
authoritative; this is the checklist to hit):
1. **Frontmatter** — `type/status/author/supersedes` + parent links (finding/slice/meta-plan); status
   encodes state (`ready-for-execution`, `revised-post-review`).
2. **Code-grounded throughout** — `file.cs:line` anchors, "verified against source," and *real* code
   (signatures, jsonc schemas), never pseudocode.
3. **Decisions *locked* with rationale** — mark author-overrides vs defaults; justify what's *dropped*
   (cite the simplicity / surgical-change standards), not only what's kept.
4. **Design ("what") separated from build order ("how")** — a **RED → GREEN → REFACTOR cycle list** is
   the spine, each cycle naming the exact test seam/file + assertions **and the mutant it must survive**
   (see `test-filter-development`).
5. **Hard scope fence** — Non-goals / Out-of-scope, restated "for the executor."
6. **Review Response table** — finding · severity · disposition · code-grounded rationale · what-changed
   (the record of surviving the adversarial pass).
7. **Executable close** — verifiable acceptance, mandatory live-cred integration gate, warnings-as-errors
   build gate, effort estimate (flag the "long tail"), critical-files-for-the-executor list.

The throughline at every tier is **spec-as-contract**: everything the implementer needs to execute
without re-deriving, reasoning preserved so it can be reviewed and defended.

## Step 3 — Review gates are non-negotiable per tier

- **Adversarial plan review** = `plan-send-review` → reviewed by a **different harness** before code. This
  is the cheapest catch point (fixes ambiguity before a line is written).
- **Adversarial code-review caboose** = the code-review workflow (`code-review-plan-create` /
  `-execute`, or `code-send-review`) → reviewed by a **different harness** after implementation.

Same-model review is theater — it shares the author's blind spots by construction. The independent gate
must run on a *different* model. Where the spec is lighter (Low→Medium→High spec effort decreases per
reader capability), the review requirement *increases* to hold total safety constant.

## Step 4 — Calibrate (close the loop, make tiering empirical)

Instrument two signals per delivered spec:
1. **Review real-findings count** — adversarial findings that were genuinely under-specified upstream.
2. **Impl-decisions-not-in-spec count** — every "the spec didn't say, so I chose X" during build.

- Both ≈ 0 on a **HIGH** spec repeatedly → you over-specified that *class*; drop it a tier next time.
- Several on a **MEDIUM** spec → under-specified; thicken it or bump to HIGH.

Over a handful of cycles this self-corrects tier assignment instead of guessing. **Over-spec smell:**
you're documenting what the compiler / type-system / test already guarantees, or restating idioms the
reader already holds — that's dead weight; trim it. Everything else earns its place.

## Adoption / teaching (easing non-expert readers in)

- **Don't start anyone at HIGH.** A HIGH spec is the *output* of expert skim-reading judgment plus a
  review round-trip — not a starting point. It intimidates because it's an endpoint.
- **Team default = MEDIUM spec + mandatory code-review caboose.** The review is the safety net that lets
  the spec be lighter than an expert's — *and* it is the teaching mechanism: every real finding shows a
  dev exactly what they under-specified. Their MEDIUM specs tighten over time with no hand-teaching, and
  the strong ones graduate toward HIGH when stakes demand it.
- **Expert configuration (HIGH spec / low review) is valid but has no independent check.** It is safe only
  while the author's judgment is the ceiling. On genuinely irreversible/high-stakes work, run the
  adversarial pass anyway — cheap insurance against the blind spot you can't see by construction.

## Composition with existing skills

- **Invoke `spec-tier` first** — it sets *how much* depth; `plan-workflow` / `writing-plans` then produce
  it and manage the kanban lifecycle.
- **`test-filter-development`** — always on at Medium+; it is the cycle spine of the plan. It supersedes
  `test-driven-development` as the call-out: the ordering is not the load-bearing part, the filter is
  (captured RED output, and a seeded mutant after GREEN).
- **`csharp-quality-developer`** — for any C# the plan drives (StyleCop, `this.`, LoggerMessage, CRLF).
- **`plan-send-review`** — the adversarial plan gate (Medium recommended, High required).
- **code-review workflow** — the adversarial caboose (Medium+ required).
- **`prompt-dotnet`** already chains `using-superpowers → test-filter-development → csharp-quality-developer`;
  `spec-tier` sits in front of it to fix the depth and the review obligation before that chain runs.

Skills are the portable source of truth; this one only routes and gates — defer to each governing skill
for its mechanics.
