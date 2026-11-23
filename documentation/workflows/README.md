# Workflows

Opinionated, repeatable AI-assisted coding workflows that use the skills in this repo. Each doc walks through one pattern end-to-end — when to reach for it, the skills it depends on, and what good output looks like.

---

## Pick a workflow by the size of the work

Not every task needs ceremony. The first decision is whether you're doing a focused fix or building something new.

### Single-prompt programming

You write a one-shot prompt, the agent responds, you move on. No formal plan, no review pipeline, no spec artifact.

**Use it for:**

- **Bug finding** and small bug fixes — "the user list isn't paginating, find out why"
- One-off questions about the codebase
- Renames, small refactors localized to one file
- Generating a unit test for an existing function
- Quick "how does this work" exploration

The cost of being wrong is low (you can re-prompt, revert, try again), so the cost of planning would exceed the cost of just doing it.

### Spec-driven coding

You write a brief, turn it into a detailed plan with its feature-spec, get an adversarial review of the plan, *then* implement, *then* review the code. See [SPEC-DRIVEN-CODE-WORKFLOW.md](SPEC-DRIVEN-CODE-WORKFLOW.md). [`plan-tier`](../../skills/plan-tier/) decides how much plan and review the work's stakes call for.

**Use it for:**

- **New feature implementation** — a route, a screen, a service, a module that didn't exist
- Migrations and architectural changes (auth swap, database move, framework upgrade)
- Anything that will affect multiple files across multiple subsystems
- Work where the cost of being wrong about the design exceeds the cost of writing a careful plan

The plan constrains the agent during implementation, and its feature-spec merges into the area's system spec, which becomes institutional memory as the solution grows over time. The two adversarial reviews (plan + code) catch the bugs and drift you and the authoring session both missed.

When the work is done, close the plan with `plan-workflow`: it moves to `planning/completed/` with its Status block, and any check that can only happen after the pull request becomes a `verify` reminder. Then raise the pull request — `ado-pr` on Azure DevOps, `gh pr create` on GitHub. A session started in `plan`, `implement` or `commit` mode stops at the close; raise the pull request yourself. See [SESSION-WORKFLOW.md](SESSION-WORKFLOW.md#ending-a-session).

For an Azure DevOps ticket, `/ticket-start {id}` plans the ticket in its own worktree, `/ticket-start {id} implement` builds and verifies it uncommitted, and `/ticket-end {id}` commits, pushes and opens a draft pull request linked to the ticket. See [SESSION-WORKFLOW.md](SESSION-WORKFLOW.md).

When several plans need to run at once, run them as a wave: see [WAVE-WORKFLOW.md](WAVE-WORKFLOW.md).

---

## Index

| Document | When to read |
|---|---|
| [SPEC-DRIVEN-CODE-WORKFLOW.md](SPEC-DRIVEN-CODE-WORKFLOW.md) | You're about to implement a feature. The top-to-bottom walkthrough: find the system spec → gather sources → write the brief → plan review → implement → code review → merge the feature-spec. |
| [PLAN-REVIEW-WORKFLOW.md](PLAN-REVIEW-WORKFLOW.md) | Mid-planning: you've drafted a plan and want a second AI (or a second session) to tear it apart before any code gets written. Three skills: `plan-send-review`, `plan-intake-review`, `plan-receive-review`. |
| [CODE-REVIEW-WORKFLOW.md](CODE-REVIEW-WORKFLOW.md) | Post-implementation: ad-hoc per-slice review (path A) or full-branch sweep with synthesis (path B). Seven skills, file-system kanban, idempotent. |
| [WAVE-WORKFLOW.md](WAVE-WORKFLOW.md) | Several plans need to run in parallel: when to write a wave, starting the lead and track sessions, where people approve, and closing the wave. |
| [SESSION-WORKFLOW.md](SESSION-WORKFLOW.md) | More than one agent works on your machine, or you start a ticket with `/ticket-start`: starting a session, choosing its mode, locks on shared ports and databases, and ending it with `/ticket-end` or a pull request. |

The terms these pages use (system spec, feature-spec, plan, brief, slice, session, mode, subject) are defined in [VOCABULARY.md](../VOCABULARY.md).

---

## The shape that ties them together

```text
[ Spec-driven workflow ]
        │
        ▼
   ┌────────────┐         ┌────────────┐         ┌────────────┐
   │  Author    │ ──────▶ │  Plan      │ ──────▶ │  Refined   │
   │  the plan  │         │  review    │         │  plan      │
   └────────────┘         └────────────┘         └─────┬──────┘
                                                       ▼
                                                ┌────────────┐
                                                │  Execute   │
                                                │  the plan  │
                                                └─────┬──────┘
                                                      ▼
                                               ┌────────────┐
                                               │  Code      │
                                               │  review    │
                                               └────────────┘
```

Both review passes are adversarial: a *different* AI runtime (or at minimum a fresh session) critiques the artifact, then a human arbitrates accept/reject. The result is a feature that survived two rounds of cross-runtime review before merging — the LLM blind spots that the authoring session rationalized around have already been challenged.

The single-prompt path skips this whole shape on purpose: small work doesn't need it, and forcing ceremony onto a five-minute task is its own kind of bug.

---

## Skills referenced by these workflows

The workflows are powered by the skills bundled in this marketplace:

- **Specs and plans:** [system-spec](../../skills/system-spec/), [system-spec-derive](../../skills/system-spec-derive/), [system-spec-backfill](../../skills/system-spec-backfill/), [plan-tier](../../skills/plan-tier/), [plan-workflow](../../skills/plan-workflow/), [plan-writing-syntax](../../skills/plan-writing-syntax/)
- **Parallel plans:** [wave-workflow](../../skills/wave-workflow/)
- **Sessions and tickets:** [session-workflow](../../skills/session-workflow/), [ticket-start](../../skills/ticket-start/), [ticket-end](../../skills/ticket-end/), [ado-pr](../../skills/ado-pr/)
- **Plan review:** [plan-send-review](../../skills/plan-send-review/), [plan-intake-review](../../skills/plan-intake-review/), [plan-receive-review](../../skills/plan-receive-review/)
- **Code review:** [code-send-review](../../skills/code-send-review/), [code-intake-review](../../skills/code-intake-review/), [code-review-apply](../../skills/code-review-apply/), [code-review-plan-create](../../skills/code-review-plan-create/), [code-review-seed-slices](../../skills/code-review-seed-slices/), [code-review-plan-execute](../../skills/code-review-plan-execute/), [code-synthesize-reviews](../../skills/code-synthesize-reviews/)

See [README.md#skills](../../README.md#skills) for the full categorized catalog.
