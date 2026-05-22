# Spec-Driven AI Coding Workflow

Spec-driven AI coding is where you put serious effort into the plan *before* a single line of code gets written. The spec is typically detailed enough to include the code you want — file names, library choices, signatures, error handling — produced through a back-and-forth with an AI agent until both of you are satisfied that the spec describes the thing you actually want.

The payoff is that the spec becomes institutional memory. It constrains the agent during implementation, it documents the *why* for the next developer (human or AI), and it gives you a concrete artifact to review adversarially before any code ships. It takes longer than "single-prompt programming" — but the output is more supportable, lower-defect, and far easier to maintain when the requirements shift.

Below is one example workflow that uses the skills in this repo to pursue spec-driven development end-to-end.

---

## 1. Gather your sources

1. Create `research/`, `documentation/`, and `planning/` folders at the project root. Add `.gitkeep` files to `documentation/` and `planning/`.
2. Add `research/` to `.gitignore` — it's working material, not project source.
3. Start a `*feature-you-want-to-create*-prompt.md` file in `planning/`. This is where you'll collect sources and draft the initial prompt that becomes the spec.
4. Gather repos to feed the agent as references. Paste their git fetch URLs into the prompt markdown so the agent knows what to clone into `research/`. These usually come in three flavors:
   - **Large component libraries** you want to use in your solution
   - **Other projects** demonstrating how to write what you're building (demos, samples, reference implementations)
   - **Projects with the style, quality, and structure you want to emulate**

---

## 2. Write the spec

Create a file at `planning/<feature-name-phase-1>-prompt.md`. Start writing your spec in the file. Using a file helps you organize your thoughts and makes it so you can feel like you can walk away without having to submit right away. Ask the agent in the prompt file to set up your branch the way you want it, and to clone the relevant projects into `research/` for reference. Give it a known-good local project as a quality baseline. Instruct it to use the skills you want applied while writing the plan.

Personal recommendations:

- `test-driven-development` (from the superpowers plugin)
- Language-specific quality skills — `csharp-quality-developer`, `typescript-pro`, `python-pro`, `angular-expert`, `react-specialist`, etc.

The recommendations above aren't exhaustive but they're the high-leverage starting set.

### Break the spec into phases

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
> Apply the `angular-architect`, `angular-expert`, and `typescript-pro` skills. Use `test-driven-development`; one feature per phase, RED-GREEN-REFACTOR per atomic unit. Charts must render in under 500 ms for a 24-hour window of data (~1,440 points).

The specific version constrains framework version, library choices, file layout, authentication strategy, routing, role model, performance targets, reference projects, and which skills to apply. There's very little room for the agent to drift.

---

## 3. Planning

Switch to **Plan mode** in whatever harness you're using (Claude Code's `/plan`, the Copilot CLI Chat extension's blue Plan badge, etc.). Start the session with a prompt like:

```text
Craft an executable plan using the test-driven-development and csharp-quality-developer
skills. Task it out with RED-GREEN-REFACTOR in mind. Reference
planning/<feature-name-phase-1>-prompt.md for the full spec for the initial phases of this project.
```

Go back and forth with the agent. Read the plan carefully. Tell it what to change, re-order, correct. Give it concrete examples when something is fundamentally wrong. When you're satisfied with the spec, follow the [Plan Review Workflow](PLAN-REVIEW-WORKFLOW.md) — that's the adversarial-review pass that catches what you and your authoring session both missed. The final step of that workflow has the agent execute the plan.

---

## 4. Implementation

If you're new to this workflow, watch the agent as it works. (Aside: putting the agent in YOLO / auto / bypass mode helps with permission exhaustion, but you don't have to be permissive — it will prompt you as it goes.) Don't be afraid to stop the agent and either:

- **Put it back into planning mode** to correct the spec (this is the preferred fix — the spec is the artifact you want correct)
- **Correct it inline** and tell the agent what to do differently

Once you get used to writing very specific specs, you'll find you can hand off and walk away while the spec executes — picking up other tasks while the current one finishes. This becomes the norm as your specs deepen.

---

## 5. Local code review

When implementation is done, have the agent commit the code, then invoke the `code-send-review` skill to queue the change for adversarial review. See the [Code Review Workflow](CODE-REVIEW-WORKFLOW.md) for the full pipeline — for a single commit it mirrors the plan-review pattern; for multiple commits there's a more advanced slice-and-dice approach (`code-review-plan-create` → `code-review-plan-execute`) that generates a per-slice action list before merge.

---

## Conclusion

By bookending the work with adversarial AI reviews — plan review before implementation, code review after — you catch bugs and drift that would otherwise slip through. This isn't an exhaustive playbook; it's a starting shape for how *you* might set up your own AI-assisted coding workflow. Adjust the skills, the phasing, and the review cadence to match your team and the stakes of the work. The most important artifact from this whole process IS the spec. It is what combats context exhaustion and leads to institutional memory and combats drift that happens without having those spec files in place.
