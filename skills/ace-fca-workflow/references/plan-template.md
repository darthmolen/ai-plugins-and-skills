# Implementation Plan Template

Use this template when creating implementation plans. Each phase should be independently executable and verifiable.

---

# Plan: [Feature/Bug Name]

## Overview
[2-3 sentence summary of what we're implementing and the approach]

## Goal
[Clear statement of the end state]

## Approach Summary
[Brief description of the chosen approach from research]

## Prerequisites
- [ ] Research document reviewed and approved
- [ ] Git worktree created (for implementation)
- [ ] Dependencies available

---

## Phase 1: [Phase Name]

### Objective
[What this phase accomplishes]

### Files to Modify
| File | Changes |
|------|---------|
| `path/to/file.ts` | [Brief description of changes] |

### Implementation Steps
1. [Specific step with code location]
2. [Next step]
3. [Continue...]

### Code Changes

#### `path/to/file.ts`
```typescript
// Before (lines X-Y)
[existing code]

// After
[new code]
```

### Verification
- [ ] Unit test: `npm test -- path/to/test.ts`
- [ ] Manual verification: [What to check]
- [ ] Existing tests pass: `npm test`

### Definition of Done
- [ ] Code changes complete
- [ ] Tests written/updated
- [ ] All tests passing
- [ ] Changes committed

---

## Phase 2: [Phase Name]

### Objective
[What this phase accomplishes]

### Dependencies
- Phase 1 complete

### Files to Modify
| File | Changes |
|------|---------|
| `path/to/file.ts` | [Brief description of changes] |

### Implementation Steps
1. [Specific step]
2. [Next step]

### Verification
- [ ] [Specific verification step]

### Definition of Done
- [ ] [Specific completion criteria]

---

## Phase 3: [Phase Name]
[Continue pattern...]

---

## Final Integration

### Integration Testing
- [ ] [Integration test 1]
- [ ] [Integration test 2]

### Manual Verification
- [ ] [Manual check 1]
- [ ] [Manual check 2]

### Documentation Updates
- [ ] [If any docs need updating]

---

## Rollback Plan
[How to revert if something goes wrong]

## Risk Assessment
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| [Risk 1] | Low/Med/High | Low/Med/High | [How to handle] |

## Status Tracking

### Current Phase: [N]
### Status: [Not Started / In Progress / Blocked / Complete]

| Phase | Status | Notes |
|-------|--------|-------|
| Phase 1 | ⬜ Not Started | |
| Phase 2 | ⬜ Not Started | |
| Phase 3 | ⬜ Not Started | |
| Integration | ⬜ Not Started | |

---

## Execution Notes
[Updated during implementation with learnings, blockers, decisions made]
