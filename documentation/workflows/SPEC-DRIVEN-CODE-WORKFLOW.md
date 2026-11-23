# Spec-Driven AI Coding Workflow

Spec-driven AI coding is where you put serious effort into the plan *before* a single line of code gets written. The plan is typically detailed enough to include the code you want — file names, library choices, signatures, error handling — produced through a back-and-forth with an AI agent until both of you are satisfied that it describes the thing you actually want.

Three documents carry the work. The **system spec** describes what the area's system does and is kept for the life of the system. The **brief** is the prompt file you write to start the work. The **plan** is the work for one change, and its **feature-spec** is the piece of spec the change adds or alters. See [VOCABULARY.md](../VOCABULARY.md).

The payoff is that the spec becomes institutional memory. The feature-spec constrains the agent during implementation, the plan documents the *why* for the next developer (human or AI), and you get a concrete artifact to review adversarially before any code ships. It takes longer than "single-prompt programming" — but the output is more supportable, lower-defect, and far easier to maintain when the requirements shift.

Below is one example workflow that uses the skills in this repo to pursue spec-driven development end-to-end.

---

## 0. Find or create the area's system spec

Look under `docs/specs/` for the system spec of the area you are changing (`system-spec`). Your plan's feature-spec will be checked against it and merged into it.

If the area has none:

- If it was built plan by plan and the plans are in `planning/`, run `system-spec-backfill`.
- Otherwise, run `system-spec-derive` to draft one from the code.
- For a new area, start one from the `system-spec` template.

---

## 1. Gather your sources

1. Create `research/`, `documentation/`, and `planning/` folders at the project root. Add `.gitkeep` files to `documentation/` and `planning/`.
2. Add `research/` to `.gitignore` — it's working material, not project source.
3. Start a `*feature-you-want-to-create*-prompt.md` file in `planning/`. This is the brief: where you'll collect sources and draft the initial prompt that becomes the plan.
4. Gather repos to feed the agent as references. Paste their git fetch URLs into the prompt markdown so the agent knows what to clone into `research/`. These usually come in three flavors:
   - **Large component libraries** you want to use in your solution
   - **Other projects** demonstrating how to write what you're building (demos, samples, reference implementations)
   - **Projects with the style, quality, and structure you want to emulate**

---

## 2. Write the brief

Create a file at `planning/<feature-name-phase-1>-prompt.md`. Start writing your brief in the file: the choices you own, and the behaviour you want, stated as spec. Using a file helps you organize your thoughts and makes it so you can feel like you can walk away without having to submit right away. Ask the agent in the prompt file to set up your branch the way you want it, and to clone the relevant projects into `research/` for reference. Give it a known-good local project as a quality baseline. Instruct it to use the skills you want applied while writing the plan.

Personal recommendations:

- `test-driven-development` (from the superpowers plugin)
- Language-specific quality skills — `csharp-quality-developer`, `typescript-developer`, `python-developer`, `angular-developer`, `react-developer`, etc.

The recommendations above aren't exhaustive but they're the high-leverage starting set.

### Break the work into phases

Keep your phasing atomic and specific. Don't pile everything into one big paragraph — that makes it hard to review and likely to fall out of context, which gives you less-than-stellar (read: buggy) code.

A phase outline can be as simple as:

> **Phase 1** — Implement a React SPA at `src/my-admin-app/` and import the following libraries for use: blah, blah, blah. Reference `research/my-react-admin-app/` for structure. Test these initial components with xyz; style to match `my-react-admin-app`.
>
> **Phase 2** — Authentication: import the MSAL library and add a guard to all routes except `api/spa-config`.
>
> **Future phases** — user administration, order management, metrics dashboards, order workflowing. Data sourced from a .NET Core API.

The last part (future phases) gives the agent full context for where the project is heading so it can scope appropriately and avoid over- or under-engineering Phase 1.

### Be as specific as possible

Specificity gives the agent a specific solution. Vagueness leads to drift.

**Vague:**

> "Write me an Angular app that helps my currently existing API show metrics to an administrator."

**Specific:**

> Build an Angular 20 admin app at `src/admin-metrics-spa/` that surfaces metrics from our existing .NET 10 API at `services/api/` (OpenAPI spec at `services/api/openapi.yaml`). Use Angular Material 20 for layout primitives, ng2-charts for time-series visualizations, and MSAL for authentication. Reference `research/contoso-admin-portal/` for project structure, component layout, and quality bar — match its standalone components, `inject()` function (no constructor DI), and signal-based state.
>
> **Routes:**
> - `/dashboard` — landing page with top-line KPIs (request count, error rate, p95 latency)
> - `/api-health` — service-by-service drill-down
> - `/audit` — read-only audit log viewer
>
> All routes require an authenticated user with the `metrics-reader` role; guard via `canActivate`. `/api/spa-config` is the only unauthenticated endpoint (returns MSAL tenant config + feature flags).
>
> Apply the `angular-architect`, `angular-developer`, and `typescript-developer` skills. Use `test-driven-development`; one feature per phase, RED-GREEN-REFACTOR per atomic unit. Charts must render in under 500 ms for a 24-hour window of data (~1,440 points).

The specific version constrains framework version, library choices, file layout, authentication strategy, routing, role model, performance targets, reference projects, and which skills to apply. There's very little room for the agent to drift.

---

## 3. Plan, with its feature-spec

Switch to **Plan mode** in whatever harness you're using (Claude Code's `/plan`, the Copilot CLI Chat extension's blue Plan badge, etc.). Start the session with a prompt like:

```text
Craft an executable plan using the plan-tier, test-driven-development and
csharp-quality-developer skills. Task it out with RED-GREEN-REFACTOR in mind. Put the behaviour
this change adds or alters in a ## Feature-spec section, checked against the area's system spec.
Reference planning/<feature-name-phase-1>-prompt.md for the brief for the initial phases of this
project.
```

Go back and forth with the agent. Read the plan carefully. Tell it what to change, re-order, correct. Give it concrete examples when something is fundamentally wrong. Check that each feature-spec block agrees with the system spec, or amends it and says why, or adds a new feature (`plan-writing-syntax`). When you're satisfied with the plan, follow the [Plan Review Workflow](PLAN-REVIEW-WORKFLOW.md) — that's the adversarial-review pass that catches what you and your authoring session both missed. The final step of that workflow has the agent execute the plan.

---

## 4. Implementation

If you're new to this workflow, watch the agent as it works. (Aside: putting the agent in YOLO / auto / bypass mode helps with permission exhaustion, but you don't have to be permissive — it will prompt you as it goes.) Don't be afraid to stop the agent and either:

- **Put it back into planning mode** to correct the plan (this is the preferred fix — the plan is the artifact you want correct)
- **Correct it inline** and tell the agent what to do differently

Once you get used to writing very specific plans, you'll find you can hand off and walk away while the plan executes — picking up other tasks while the current one finishes. This becomes the norm as your plans deepen.

---

## 5. Local code review

When implementation is done, have the agent commit the code, then invoke the `code-send-review` skill to queue the change for adversarial review. See the [Code Review Workflow](CODE-REVIEW-WORKFLOW.md) for the full pipeline — for a single commit it mirrors the plan-review pattern; for multiple commits there's a more advanced slice-and-dice approach (`code-review-plan-create` → `code-review-plan-execute`) that generates a per-slice action list before merge.

---

## 6. Merge the feature-spec into the system spec, and close the plan

In the same pull request as the code, merge each feature-spec block into the system spec: leave the text as it is for `agree`, replace it for `amend`, and add a new feature section for `add`. Set each delivered feature's status, and run `spec-table --fix` (`system-spec`). Then close the plan with `plan-workflow`: the Status block, the move to `completed/`, and a `verify` reminder for any check that can only happen after the pull request. The plan is not edited again; the next change to this feature is a new plan with its own feature-spec.

Raise the pull request with `pr-create`, on Azure DevOps or GitHub. A session started in `plan`, `implement` or `commit` mode stops at the close, and the pull request is yours to raise. See [SESSION-WORKFLOW.md](SESSION-WORKFLOW.md#ending-a-session).

---

## Scaling up: specs, plans and waves

The steps above take one plan from prompt to merged code. Three skills cover the scales around it, and each can be used without the others:

| Skill | Scale | What it holds |
|---|---|---|
| [`system-spec`](../../skills/system-spec/) | The product | One evergreen spec per area, under `docs/specs/`, describing the target system. Releases are statuses and interim blocks on its features, never trimmed copies. |
| [`plan-workflow`](../../skills/plan-workflow/) | One piece of work | The plan on the `planning/` board. A plan names the spec features it delivers in `delivers:`. |
| [`wave-workflow`](../../skills/wave-workflow/) | Parallel plans | Several plans run at once on separate tracks, each in its own session, under one lead session that verifies and pushes. See [WAVE-WORKFLOW.md](WAVE-WORKFLOW.md). |

When more than one agent works on your machine at once, each runs as a `session-workflow` session with its own mode and locks; see [SESSION-WORKFLOW.md](SESSION-WORKFLOW.md).

[`plan-tier`](../../skills/plan-tier/) decides how deep a plan goes and records it as `tier:` in the frontmatter. `plan-workflow` ships `scripts/plan-doc.cs`, which enforces the frontmatter contract from [`plan-writing-syntax`](../../skills/plan-writing-syntax/) and needs the .NET 10 SDK:

```powershell
dotnet run "<plan-workflow dir>\scripts\plan-doc.cs" -- validate --path planning              # check the board
dotnet run "<plan-workflow dir>\scripts\plan-doc.cs" -- close --doc <path> --to completed ... # move + status in one step
dotnet run "<plan-workflow dir>\scripts\plan-doc.cs" -- spec-table --specs docs --path planning --fix
```

---

## Conclusion

By bookending the work with adversarial AI reviews — plan review before implementation, code review after — you catch bugs and drift that would otherwise slip through. This isn't an exhaustive playbook; it's a starting shape for how *you* might set up your own AI-assisted coding workflow. Adjust the skills, the phasing, and the review cadence to match your team and the stakes of the work. The most important artifact from this whole process is the spec: the system spec, kept current by each plan's feature-spec. It is what combats context exhaustion, gives you institutional memory, and prevents the drift that sets in when no document says what the system does.
