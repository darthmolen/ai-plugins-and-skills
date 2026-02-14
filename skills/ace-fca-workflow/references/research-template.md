# Research Template

Use this template when documenting research findings. The goal is to capture everything needed to create an implementation plan without re-exploring the codebase.

---

# Research: [Feature/Bug Name]

## Summary
[2-3 sentence high-level summary of findings]

## Goal
[What we're trying to accomplish - the bug to fix or feature to add]

## Codebase Overview

### Repository Structure
```
relevant/
├── directories/
│   └── and files
```

### Key Files
| File | Purpose | Relevance |
|------|---------|-----------|
| `path/to/file.ts` | [What it does] | [Why it matters for this task] |
| `path/to/other.ts` | [What it does] | [Why it matters for this task] |

## Information Flow

### Current Behavior
[How the system currently works in the area we're modifying]

```
[Component A] → [Component B] → [Component C]
     │              │               │
   input         transform        output
```

### Data Flow
[Trace the data/request through the system]
1. Entry point: `function()` in `file.ts`
2. Processing: `handler()` in `handler.ts`
3. Output: `response()` in `api.ts`

## Problem Analysis

### Root Cause (for bugs)
[What's actually causing the issue]

### Current Limitations (for features)
[What doesn't exist that needs to exist]

### Affected Code Paths
- `path/to/affected/code.ts:45-67` - [Why affected]
- `path/to/other/code.ts:123` - [Why affected]

## Codebase Conventions

### Testing Patterns
[How tests are written in this codebase]
- Test location: `__tests__/` or `*.test.ts`
- Test framework: [Jest/Vitest/etc.]
- Mocking patterns: [How mocks are done]

### Code Style
[Relevant style patterns to follow]
- Error handling: [Pattern used]
- Logging: [Pattern used]
- Type definitions: [Where/how defined]

### Similar Implementations
[Reference existing code that does something similar]
- `path/to/similar.ts` - [How it's similar and what we can learn]

## Potential Approaches

### Approach 1: [Name]
- **Description**: [Brief description]
- **Pros**: [Advantages]
- **Cons**: [Disadvantages]
- **Affected files**: [List]

### Approach 2: [Name]
- **Description**: [Brief description]
- **Pros**: [Advantages]
- **Cons**: [Disadvantages]
- **Affected files**: [List]

### Recommended Approach
[Which approach and why]

## Key Code Snippets

### [Snippet Name]
```typescript
// path/to/file.ts:45-60
[relevant code snippet]
```
[Why this snippet is important]

## Dependencies & Constraints

### External Dependencies
[Libraries or services this code depends on]

### Internal Dependencies
[Other parts of the codebase this code depends on]

### Constraints
[Technical or business constraints to be aware of]

## Open Questions
- [ ] [Question that needs answering]
- [ ] [Another question]

## Notes
[Any other relevant observations]
