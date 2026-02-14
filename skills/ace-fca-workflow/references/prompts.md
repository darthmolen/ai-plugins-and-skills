# Example Prompts

Use these prompts as starting points. Customize based on your specific codebase and task.

---

## Research Phase Prompt

```
I need you to research [CODEBASE] to understand how to [TASK/BUG].

Context: [Brief context about the codebase and what we're trying to accomplish]

First, read the research template at `references/research-template.md` for the expected output structure.

Then create the task folder and investigate:
1. The relevant file structure and key files involved in [AREA]
2. How [CURRENT FUNCTIONALITY] currently works
3. The data/request flow through the system
4. [FOR BUGS: What might be causing [SYMPTOM]]
5. [FOR FEATURES: What would need to change to support [FEATURE]]
6. Testing patterns used in this codebase
7. Similar implementations we can reference

Use subagents for exploration to keep context clean. Focus on understanding HOW the system works, not deciding WHAT to change yet.

Output your findings to `planning/backlog/{task-name}/research.md` following the template structure.

Be thorough but concise. Include file paths and line numbers. Note any conventions or patterns we should follow.
```

### Research Steering Additions

If initial research misses the mark, add steering:

```
Focus specifically on:
- [SPECIFIC AREA] - the previous research missed [WHAT]
- Look at how [SIMILAR FEATURE] was implemented in [FILE]
- Trace the code path starting from [ENTRY POINT]
- Pay attention to [SPECIFIC PATTERN/CONVENTION]
```

---

## Planning Phase Prompt

```
Based on the research in `planning/backlog/{task-name}/research.md`, create an implementation plan for [TASK].

First, read the plan template at `references/plan-template.md` for the expected output structure.

Requirements:
- Break the work into discrete phases (3-5 phases typically)
- Each phase should be independently testable
- Include specific file paths and function names
- Match the testing patterns from the codebase
- Each phase should be completable in one context session

For each phase, include:
1. Objective (what this phase accomplishes)
2. Files to modify with specific changes
3. Step-by-step implementation instructions
4. Verification steps (tests to run, manual checks)
5. Definition of done

Also include:
- Prerequisites
- Final integration testing plan
- Rollback plan
- Status tracking section

Output to `planning/backlog/{task-name}/plan.md`. Be precise - this plan should be executable by someone who hasn't seen the research.

After human review, the folder will be moved to `planning/in-progress/{task-name}/` to begin implementation.
```

---

## Implementation Phase Prompt

```
Execute Phase [N] of the implementation plan in `planning/in-progress/{task-name}/plan.md`.

[PASTE CURRENT PHASE DETAILS OR REFERENCE plan.md]

Instructions:
1. Read the phase details carefully
2. Implement exactly as specified
3. Run the verification steps
4. Report any deviations or issues

If you encounter problems:
- Document what went wrong
- Propose alternatives if the plan needs adjustment
- Do not proceed to the next phase without verification passing

After completion:
1. Update status in `planning/in-progress/{task-name}/status.md`
2. Commit changes with message: "[Phase N] [Brief description]"

When all phases complete, move folder to `planning/completed/{task-name}/`.
```

### Implementation Continuation Prompt

```
Continue implementation from Phase [N].

Load context from:
- `planning/in-progress/{task-name}/plan.md`
- `planning/in-progress/{task-name}/status.md`

Current phase objective: [FROM PLAN]
Previous phases completed: [LIST]

Proceed with implementation. If context is getting full (>60%), compact current status to `status.md` and note that we should start fresh for the next phase.
```

---

## Compaction Prompt

```
We need to compact the current state before continuing.

Please update `planning/in-progress/{task-name}/status.md` with:
1. Goal: What we're trying to accomplish
2. Approach: The approach we're taking
3. Progress: What's been done so far
4. Current state: Where we are now, including any current failures or blockers
5. Next steps: What needs to happen next
6. Key decisions: Any important decisions made and why

Be concise but complete - this will be the only context for the next session.
```

---

## Subagent Research Prompt

For delegating exploration to keep main context clean:

```
Use a subagent to explore [SPECIFIC AREA].

Task for subagent:
[SPECIFIC EXPLORATION TASK]

The subagent should return a structured summary including:
- Files found and their purposes
- Relevant code snippets (with file paths and line numbers)
- How the components connect
- Key observations

Keep the summary under [N] lines. Focus on [PRIORITY ASPECTS].
```

---

## Recovery Prompts

### When Stuck

```
We seem to be stuck on [PROBLEM].

Current context is likely polluted. Please:
1. Update `planning/in-progress/{task-name}/status.md` with current state
2. Note what approaches have been tried
3. Identify what information is missing or incorrect

We'll start fresh with a new approach in the next session.
```

### When Plan Needs Revision

```
Phase [N] has revealed the plan needs adjustment.

Issue: [WHAT WENT WRONG]
Learning: [WHAT WE NOW KNOW]

Please update `planning/in-progress/{task-name}/plan.md` with:
1. Revised approach for remaining phases
2. New phase breakdown if needed
3. Updated verification steps

Then continue implementation from the revised plan.
```
