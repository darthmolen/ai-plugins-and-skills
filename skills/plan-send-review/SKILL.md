---
name: plan-send-review
description: Use when you have authored a plan and want the other AI agent (Claude or Copilot) to review it - copies the active plan to planning/needs-review/ for adversarial review
---

# Send Plan for Review

Copy the active session's plan file to the project's review queue so the other AI agent can review it.

## Plan File Locations

Find the active plan in one of these locations:

| Agent | Location | Notes |
|-------|----------|-------|
| Claude Code | `~/.claude/plans/<name>.md` | Active plan from current session |
| Copilot CLI | `~/.copilot/session-state/<session-id>/plan.md` | Session plan file |
| Project | `planning/**/*.md` (excluding `needs-review/`) | Any plan in the project tree |

## Process

### Step 1: Locate the Plan

```bash
# Claude plans
ls -t ~/.claude/plans/*.md 2>/dev/null | head -5

# Copilot session plans (most recent)
ls -t ~/.copilot/session-state/*/plan.md 2>/dev/null | head -5

# Project plans (excluding the review pipeline)
find planning/ -name "*.md" -not -path "*/needs-review/*" 2>/dev/null
```

If multiple candidates exist, ask the user which plan to send.

### Step 2: Name the Review File

1. Extract the first H1 heading (`# ...`) from the plan
2. Kebab-case it: lowercase, spaces to hyphens, strip special characters
3. Prepend today's date: `YYYY-MM-DD-<slug>.md`
4. If a file with that name already exists in `planning/needs-review/`, append `-2`, `-3`, etc.
5. If no H1 heading exists, ask the user for a descriptive name

Example: `2026-03-14-streaming-response-architecture.md`

### Step 3: Copy to Review Queue

```bash
cp <source-plan> planning/needs-review/YYYY-MM-DD-slug.md
```

**Copy, not move.** The original plan stays in place. The author continues working with their copy.

### Step 4: Confirm

Report to the user:
- Source file path
- Destination file path
- File size (sanity check it copied correctly)

## Do Not

- Move the original plan (copy only)
- Send plans that are clearly incomplete (no tasks, no architecture section) without warning the user
- Modify the plan content during copy
