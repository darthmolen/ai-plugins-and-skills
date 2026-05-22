# Plan Review Workflow

An AI-in-the-loop, human-in-the-loop adversarial review pipeline for plans and specs. You author the plan in your primary session, hand it off to a second agent (or a second session of the same agent) for adversarial critique, then come back to the original session to walk findings with the author agent + you. The human stays in the loop at every gate: you decide what to accept, merge, or reject before any code is written.

> **Working draft.** Co-located with the [code review workflow](CODE-REVIEW-WORKFLOW.md). The two pipelines share the same shape (file-system kanban, idempotent skills, multi-agent fan-out) and the same end-state: a spec or branch the human is genuinely confident in.

---

## Why it exists

A single LLM rationalizes around its own assumptions — it will happily defend a plan it just wrote because the context that produced the plan is the same context that's evaluating it. Two LLMs with different system prompts and no shared scratchpad catch the blind spots. You stay in the loop to arbitrate, because the reviewer is allowed to be wrong too.

The output is a plan that survived adversarial review **before** anyone wrote code, with every decision visible: what the reviewer flagged, why you accepted or rejected it, and what made it into the final spec.

---

## The three skills

| Skill | Role | Where it writes |
|---|---|---|
| `plan-send-review` | You authored a plan and want a second pair of eyes | `planning/needs-review/<plan>.md` |
| `plan-intake-review` | Reviewer session: picks up a queued plan, dispatches the plan-reviewer subagent, appends adversarial findings | `planning/needs-review/{in-progress,reviewed}/<plan>.md` |
| `plan-receive-review` | Author session: walks each finding with you, classifies accept/merge/reject, merges accepted edits into the canonical plan | edits the original plan in `planning/in_progress/` |

---

## The walkthrough

### 1. Get into planning mode

Use planning mode in whatever harness you're in — Claude Code's `/plan` toggle, the Copilot CLI Chat extension's blue Plan badge, or any equivalent. Planning mode keeps the agent honest: read-only tools, no source edits, focus is the artifact.

### 2. Develop a detailed spec or plan

Author the plan in `planning/in_progress/phase_<name>_<date>.md` (or wherever your project's convention puts it). Spell out:

- Objective + success criteria
- Files affected (create / modify / delete)
- Phases / steps with concrete deliverables
- Out-of-scope items
- Rollback strategy

The reviewer's value scales with the detail in the plan — vague plans get vague reviews.

### 3. Send for review

```text
/plan-send-review
```

`plan-send-review` copies the active plan into `planning/needs-review/` with frontmatter that captures `source_path`, `created`, `status: queued`. The original stays untouched in `planning/in_progress/` — what gets reviewed is a snapshot.

### 4. Switch sessions, intake the review

Bounce to a second session — same agent in a fresh context, OR a different harness entirely (Claude reviews Copilot, Copilot reviews Claude). The cross-runtime case is the high-value one: different model, different system prompt, fewer shared assumptions.

In the second session:

```text
/plan-intake-review
```

`plan-intake-review` moves the queued plan to `planning/needs-review/in-progress/`, dispatches the **plan-reviewer subagent** (the adversarial reviewer template at [plan-intake-review/plan-reviewer.md](../../skills/plan-intake-review/plan-reviewer.md)), and waits for findings. The reviewer is briefed to look for: missing edge cases, unstated assumptions, scope creep, overlooked rollback paths, anything that smells like wishful thinking.

### 5. Findings get appended; plan moves to reviewed

The reviewer's findings are appended to the plan file as a `## Review Findings` section, then the file moves to `planning/needs-review/reviewed/`. Each finding carries severity (critical / major / minor / nit) and a specific recommendation. Nothing in the original plan is changed yet — the review is additive.

### 6. Receive review, walk findings, merge, execute

Back in the original (authoring) session:

```text
/plan-receive-review
```

`plan-receive-review` reads the reviewed file and walks every finding with you. For each one it presents:

- The reviewer's claim
- The reviewer's recommendation
- The receive-side agent's technical evaluation (often: "I disagree with point 3 because X, but point 5 is right and we should change the rollback section")
- A prompt: **accept / merge / reject / flag**

You arbitrate. Accepts get merged back into the canonical `planning/in_progress/<plan>.md`. Rejects are logged with reason. Flags surface follow-up backlog items.

When all findings are walked, the agent exits review mode, presents the final plan, and offers to execute it. From here it's normal implementation work — but the plan you're executing has now survived a round of adversarial critique.

---

## Directory layout

```
planning/
├── in_progress/
│   └── phase_<name>_<date>.md       ← canonical plan, edited by plan-receive-review
├── needs-review/
│   ├── <plan>.md                    ← status: queued — copied here by plan-send-review
│   ├── in-progress/
│   │   └── <plan>.md                ← status: reviewing | failed
│   └── reviewed/
│       └── <plan>.md                ← status: reviewed — findings appended, ready for receive
└── completed/
    └── phase_<name>_<date>.md       ← after execution finishes
```

The reviewed copy is a permanent record — it doesn't move back to `in_progress/` even after merge. That gives you an audit trail: original plan, reviewer findings, what got merged, what got rejected.

---

## Handoff to code review

Plan review verifies the *spec*. Code review verifies the *implementation matches the spec, and the implementation is itself well-formed*. They chain naturally:

```
plan-send-review → plan-intake-review → plan-receive-review
                  ↓
              (execute the plan, write the code)
                  ↓
code-send-review  → code-intake-review  → code-review-apply
```

When you finish the implementation session, and it's code based, commit, then start the code review workflow with `code-send-review` (ad-hoc, per-slice) or `code-review-plan-create` (full-branch sweep). See [CODE-REVIEW-WORKFLOW.md](CODE-REVIEW-WORKFLOW.md) for the full state machine.

The two workflows share the same kanban shape — `needs-review/{in-progress,reviewed}/` — so the muscle memory transfers.

---

## When to use this vs. just shipping

**Use it when:**

- The plan touches multiple subsystems, has a non-obvious migration, or involves a decision that's expensive to reverse.
- You're new to the area and want a second opinion before committing to an approach.
- The plan introduces architectural patterns the project hasn't seen before.
- Stakes are high enough that one extra agent round trip is cheap insurance.

**Skip it when:**

- The plan is a one-paragraph "fix this typo" task.
- You've already designed the same thing three times and the reviewer would just repeat what you already know.
- You're prototyping and intend to throw the code away.

The ceremony is overhead; pay it when the cost of being wrong about the plan exceeds the cost of the second session.

---

## What this workflow is NOT

- **Not a substitute for human review.** The author and reviewer are both LLMs. You — the human — are the arbiter. If you blindly accept every finding, you've added cost without adding judgment.
- **Not synchronous.** Intake can run on a `/loop` interval in a background session; plans can sit in `needs-review/` for a while. That's expected.
- **Not a one-way ratchet.** If the receive-side agent disagrees with a finding and you agree with the receive-side agent, reject it. The reviewer is allowed to be wrong.
- **Not a CI gate.** Findings are advisory. There's no "passed review" flag — the gate is your `accept/reject` walkthrough.

---

## Related

- [CODE-REVIEW-WORKFLOW.md](CODE-REVIEW-WORKFLOW.md) — the post-implementation companion pipeline
- [plan-send-review](../../skills/plan-send-review/) · [plan-intake-review](../../skills/plan-intake-review/) · [plan-receive-review](../../skills/plan-receive-review/) — the three skills
- [plan-reviewer.md](../../skills/plan-intake-review/plan-reviewer.md) — the adversarial reviewer subagent template
