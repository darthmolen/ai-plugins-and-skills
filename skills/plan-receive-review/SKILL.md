---
name: plan-receive-review
description: Use when a plan you authored has been reviewed and is waiting in planning/needs-review/reviewed/ - retrieves reviewed plan, evaluates each review point with technical rigor, and presents findings with recommended actions
metadata:
  category: review-helpers
  order: 12
---
# Receive Plan Review

Pick up a reviewed plan, evaluate each review point against the actual plan content, and present findings to the user with recommended actions.

**Core principle:** Same as receiving-code-review — verify before implementing. No performative agreement. Technical rigor always.

## Process

### Step 1: Check for Reviewed Plans

```bash
ls planning/needs-review/reviewed/*.md 2>/dev/null | grep -v .gitkeep
```

- **No files:** Do nothing. Return silently. (May be running on a loop.)
- **One file:** Proceed.
- **Multiple files:** Ask user which one to process (unless told to process all).

### Step 2: Move to Completed

```bash
mv planning/needs-review/reviewed/<file>.md planning/needs-review/completed/<file>.md
```

**Move BEFORE processing.** Prevents double-pickup. The completed copy is the audit trail — never modify it.

### Step 3: Read the Review

Read the `## Plan Review` section appended to the plan file. Parse each issue by severity.

### Step 4: Evaluate Each Point

For EACH review item, follow the receiving-code-review pattern:

```
1. READ the reviewer's point without reacting
2. VERIFY against the actual plan content
3. EVALUATE: Is the reviewer technically correct? Or do they lack context?
4. CLASSIFY:
   - ACCEPT: Reviewer is right. Plan needs revision here.
   - MERGE: Reviewer's idea enriches the plan. Incorporate partially or adapt
           rather than wholesale replacement. Show the original text, the
           reviewer's suggestion, and the merged result.
   - REJECT: Reviewer is wrong or lacks context. State technical reasoning.
   - FLAG: Cannot determine without user input. Present both sides.
```

### Step 5: Present Findings

Present to the user grouped by classification:

```markdown
## Plan Review Response: <plan-title>

### Accepted (plan revision needed)
For each accepted item:
- **[Severity] Issue:** <one-line summary>
- **Reviewer said:** <their point>
- **Verification:** <what you checked>
- **Planned revision:** <what changes to the plan>

### Merged (adapted into plan)
For each merged item:
- **[Severity] Issue:** <one-line summary>
- **Reviewer said:** <their suggestion>
- **Original plan text:** <what the plan currently says>
- **Merged result:** <how the idea was incorporated/adapted>

### Rejected (with reasoning)
For each rejected item:
- **[Severity] Issue:** <one-line summary>
- **Reviewer said:** <their point>
- **Why rejected:** <technical reasoning>
- **Evidence:** <what in the plan/codebase supports rejection>

### Flagged (user decision needed)
For each flagged item:
- **[Severity] Issue:** <one-line summary>
- **Reviewer said:** <their point>
- **Counterargument:** <why the plan might be correct as-is>
- **Risk if ignored:** <what could go wrong>
- **Recommendation:** <your lean, if any>

### Summary
- Accepted: N items (plan revisions needed)
- Merged: N items (adapted into plan)
- Rejected: N items (no action)
- Flagged: N items (awaiting user decision)
```

### Step 6: Await User Direction

After presenting findings:
- Wait for user to decide on flagged items
- If user says "apply all accepted and merged": revise the ORIGINAL plan (in its source location)
- If user wants to discuss: discuss with technical reasoning

## Where to Apply Revisions

Revisions go to the ORIGINAL plan in its source location:
- Claude: `~/.claude/plans/<name>.md`
- Copilot: `~/.copilot/session-state/<session-id>/plan.md`
- Project: `planning/<path>/<name>.md`

The `completed/` copy is the audit trail. Do not modify it.

## Forbidden Responses

Same rules as receiving-code-review:
- No "Great feedback!" or "Excellent points!"
- No "You're absolutely right!"
- No implementing before verifying
- No accepting all items without evaluation
- No thanking the reviewer

## Do Not

- Modify the completed/ copy (it is the audit trail)
- Accept all reviewer points without verification
- Reject points just because they create more work
- Skip flagged items — if you are unsure, the user decides
- Perform wholesale replacement when a merge would preserve the plan's original intent
