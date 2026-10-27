---
name: ace-fca-workflow
description: Advanced Context Engineering with Frequent Intentional Compaction (ACE-FCA) for complex coding tasks. Use this skill when working on brownfield codebases, large repos (100k+ LOC), complex bugs, multi-file refactors, or features requiring deep codebase understanding. Triggers include requests to fix bugs in unfamiliar code, implement features in large codebases, understand complex code flows, or when the user mentions "research plan implement", "ACE-FCA", or "frequent compaction". This workflow prevents context window exhaustion and produces high-quality, reviewable artifacts.
metadata:
  category: tools
  order: 2
---
# ACE-FCA Workflow

Frequent Intentional Compaction (FIC) is a context engineering approach for coding agents that maintains context utilization at 40-60% by splitting work into discrete, compacted phases.

## Core Principle

Context window contents are the ONLY lever affecting output quality. Optimize for:
1. **Correctness** - No incorrect information in context
2. **Completeness** - All necessary information present  
3. **Size** - Minimal noise, maximum signal
4. **Trajectory** - Context guides toward the goal

## Folder Structure

All artifacts are managed in a `planning/` folder with kanban-style subdirectories:

```
planning/
├── evidence/          # This skill's working folders — research.md, plan.md, status.md
│   └── feature-name/
├── backlog/           # Deferred: the stub or plan this work came from
├── in-progress/       # The plan document, once execution starts
└── completed/         # The plan document, once it ships
```

**Why `evidence/` and not `backlog/`.** Every `*.md` under `planning/` carries the frontmatter
contract, and `backlog/` now means a *status* — deferred — that any plan or stub may hold
(`plan-writing-syntax`). A `{task-name}/` folder of research notes is neither. `planning/evidence/` is exempt
from the contract by design: artifacts a plan produced and its Status block cites.

**File movement:** the research and plan artifacts stay put in `planning/evidence/{task-name}/`
for the life of the task and are cited by name. It is the **plan document** that moves through
`planning/` → `in-progress/` → `completed/`, under `plan-workflow`.

## Workflow Overview

```
┌──────────┐     ┌──────────┐     ┌─────────────┐
│ RESEARCH │ ──► │   PLAN   │ ──► │ IMPLEMENT   │
│          │     │          │     │ (per phase) │
└──────────┘     └──────────┘     └─────────────┘
     │                │                  │
     ▼                ▼                  ▼
 research.md      plan.md          code + tests
```

Each step produces a **compacted artifact** that feeds the next step with clean context.

## When to Use This Workflow

**Use ACE-FCA when:**
- Working in brownfield/established codebases
- Codebase exceeds ~50k LOC
- Bug/feature requires understanding multiple subsystems
- Initial attempts are failing or producing slop
- Task estimated at >4 hours for a human developer

**Skip to planning when:**
- Codebase is small and well-understood
- Change is localized to 1-2 files
- Pattern is clearly established

## Phase 1: Research

**Goal**: Understand the codebase, relevant files, information flow, and potential causes.

**Process**:
1. Start with a fresh context
2. Read `references/research-template.md` for output structure
3. Create task folder: `planning/evidence/{task-name}/`
4. Explore codebase structure, dependencies, and relevant files
5. Write findings to `planning/evidence/{task-name}/research.md`
6. Human reviews research before proceeding

**Key principles**:
- Use subagents for exploration to keep main context clean
- Focus on HOW the system works, not WHAT to change
- Include file paths and relevant code snippets
- Note conventions, patterns, and testing approaches used in the codebase
- If research seems wrong, discard and restart with more steering

## Phase 2: Plan

**Goal**: Create a precise, phase-by-phase implementation plan.

**Process**:
1. Start with fresh context
2. Read `references/plan-template.md` for output structure
3. Load `planning/evidence/{task-name}/research.md` into context
4. Design implementation approach based on research
5. Break into discrete phases with verification steps
6. Write plan to `planning/evidence/{task-name}/plan.md`
7. Human reviews plan before proceeding
8. Write the plan document to `planning/in-progress/` when ready to implement, citing the artifacts by name

**Key principles**:
- Each phase should be independently verifiable
- Include specific file paths and function names
- Prescribe testing strategy matching codebase conventions
- Phases should be small enough to complete in one context session

## Phase 3: Implement

**Goal**: Execute plan phase-by-phase, compacting after each phase.

**Process**:
1. Start with fresh context
2. Load `planning/in-progress/{task-name}/plan.md` (research available if needed)
3. Execute current phase
4. Run prescribed tests/verification
5. Update status in `planning/in-progress/{task-name}/status.md`
6. Commit code changes
7. Repeat for each phase
8. Move folder to `planning/completed/{task-name}/` when done

**Key principles**:
- One phase per context session when possible
- After each phase: commit, update status.md, compact
- If phase fails, document learnings and restart that phase
- Use git worktrees for implementation (research/planning can use main)

## Compaction Output Format

A good compaction artifact includes:

```markdown
# [Title: Bug/Feature Name]

## Goal
[One-sentence summary of what we're trying to accomplish]

## Context
[2-3 sentences on relevant background]

## Key Findings / Decisions
- [Finding 1 with file path if relevant]
- [Finding 2]
- [Decision made and rationale]

## Current Status
[What's done, what's next]

## Open Questions
- [Any unresolved items]
```

## Human Review Points

High-leverage human review is critical. Review effort follows this priority:

```
Research errors → thousands of bad LOC
Plan errors → hundreds of bad LOC  
Code errors → individual bad lines
```

**Review research for**: Incorrect assumptions, missed subsystems, wrong mental model
**Review plan for**: Missed edge cases, wrong approach, unrealistic phases
**Review code for**: Correctness, style, tests pass

## Troubleshooting

**Agent spinning or producing slop?**
- Context likely polluted—restart with fresh context
- Check if context utilization exceeded 60%
- Verify research/plan artifacts are correct

**Research keeps missing the mark?**
- Add more steering in prompt
- Be specific about what aspects to investigate
- Try different search patterns or entry points

**Implementation diverging from plan?**
- Stop, compact current state, restart phase
- Plan may need revision—return to planning phase

## References

- `references/research-template.md` - Detailed research output template
- `references/plan-template.md` - Implementation plan template  
- `references/prompts.md` - Example prompts for each phase
