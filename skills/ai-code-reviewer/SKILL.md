---
name: ai-code-reviewer
description: >
  Cross-agent code review using Claude Code and GitHub Copilot CLI.
  Use when the user wants a code review of current changes, asks for a
  second opinion on code, mentions "cross-review", "ai code review", or
  wants one AI agent to review code written by another. Requires both
  Claude Code and Copilot CLI to be installed for full functionality.
allowed-tools: Read Write Edit Bash Glob Grep
---

# AI Code Reviewer

Cross-agent code review: use the **opposite** AI coding tool to review the current code changes. If the user wrote code with Claude Code, Copilot CLI reviews it. If the user wrote code with Copilot, Claude Code reviews it.

## Workflow

Execute these steps in order. **Do not skip steps.**

### Step 0: Detect Installed Tools

Run the detection script to check if both tools are available:

```bash
bash skills/ai-code-reviewer/scripts/detect-tools.sh
```

Report the results to the user. If the status is anything other than `BOTH_AVAILABLE`, inform the user which tool is missing and how to install it, then **stop**. Do not proceed to Step 1.

### Step 1: Determine the Authoring Tool

Determine which AI tool the user is using to **write** code in this session. The reviewing tool will be the opposite one.

**How to determine:**

1. Check the conversation context for signals:
   - If this session is running inside Claude Code, the authoring tool is **Claude Code** and the reviewer is **Copilot CLI**.
   - If the user mentions they are using Copilot or GitHub Copilot to write code, the authoring tool is **Copilot** and the reviewer is **Claude Code**.
2. If the authoring tool cannot be determined from context, ask the user:

> Which AI tool are you using to write code in this session?
> 1. **Claude Code** (Copilot CLI will review)
> 2. **GitHub Copilot CLI** (Claude Code will review)

Record the answer as `AUTHORING_TOOL` (either `claude-code` or `copilot-cli`) and `REVIEWING_TOOL` (the opposite).

### Step 2: Determine Scope of Review

Ask the user the following to understand what needs review:

1. **What are you working on?** (feature name, bug fix, refactor, etc.)
2. **Is there a plan or spec?** If yes, ask where it is located (e.g., `planning/in-progress/<task>/plan.md`).

Capture:
- `WORK_DESCRIPTION`: Brief summary of what the user is working on.
- `PLAN_PATH`: Path to the plan file, or "none" if no plan exists.

Then gather the current changes by running:

```bash
git diff --stat
```

and:

```bash
git diff
```

If there are no changes in `git diff`, also check for staged changes:

```bash
git diff --cached
```

If there are still no changes, inform the user there is nothing to review and **stop**.

Store the diff output as `CODE_DIFF`.

### Step 3: Generate and Execute Review Script

Create the `planning/code-review/` directory if it does not exist:

```bash
mkdir -p planning/code-review
```

Generate a timestamped review filename:

```bash
REVIEW_FILE="planning/code-review/review-$(date +%Y%m%d-%H%M%S).md"
```

#### Step 3a: Review with Copilot CLI (when user authored with Claude Code)

If `REVIEWING_TOOL` is `copilot-cli`, write and execute the following script:

```bash
#!/usr/bin/env bash
set -euo pipefail

REVIEW_FILE="REVIEW_FILE_PLACEHOLDER"
WORK_DESCRIPTION="WORK_DESCRIPTION_PLACEHOLDER"
PLAN_CONTEXT="PLAN_CONTEXT_PLACEHOLDER"
CODE_DIFF=$(git diff)
if [ -z "$CODE_DIFF" ]; then
    CODE_DIFF=$(git diff --cached)
fi

REVIEW_PROMPT="You are a senior code reviewer. Review the following code changes.

## Context
Work description: ${WORK_DESCRIPTION}
${PLAN_CONTEXT}

## Instructions
1. Review the diff for correctness, security, performance, and maintainability.
2. Flag any bugs, logic errors, or potential issues.
3. Note any deviations from the plan (if a plan was provided).
4. Suggest improvements where appropriate.
5. Write your full review as a markdown document.
6. Be specific: reference file names and line numbers from the diff.

## Code Changes
\`\`\`diff
${CODE_DIFF}
\`\`\`

Write your complete review to stdout in markdown format."

gh copilot suggest -t shell "echo 'Starting review'" >/dev/null 2>&1 || true
echo "$REVIEW_PROMPT" | gh copilot explain --stdin 2>/dev/null | tee "$REVIEW_FILE" || {
    # Fallback: use gh copilot with -p flag and agent mode
    gh copilot -p "$REVIEW_PROMPT" --yolo --agent 2>&1 | tee "$REVIEW_FILE"
}

echo ""
echo "Review written to: $REVIEW_FILE"
```

Replace the placeholders with actual values before executing. If `PLAN_PATH` is not "none", set `PLAN_CONTEXT` to `"Plan reference: <PLAN_PATH>"`, otherwise set it to an empty string.

#### Step 3b: Review with Claude Code (when user authored with Copilot)

If `REVIEWING_TOOL` is `claude-code`, write and execute the following script:

```bash
#!/usr/bin/env bash
set -euo pipefail

REVIEW_FILE="REVIEW_FILE_PLACEHOLDER"
WORK_DESCRIPTION="WORK_DESCRIPTION_PLACEHOLDER"
PLAN_CONTEXT="PLAN_CONTEXT_PLACEHOLDER"
CODE_DIFF=$(git diff)
if [ -z "$CODE_DIFF" ]; then
    CODE_DIFF=$(git diff --cached)
fi

REVIEW_PROMPT="You are a senior code reviewer. Review the following code changes.

## Context
Work description: ${WORK_DESCRIPTION}
${PLAN_CONTEXT}

## Instructions
1. Review the diff for correctness, security, performance, and maintainability.
2. Flag any bugs, logic errors, or potential issues.
3. Note any deviations from the plan (if a plan was provided).
4. Suggest improvements where appropriate.
5. Write your full review as a markdown document.
6. Be specific: reference file names and line numbers from the diff.

## Code Changes
\`\`\`diff
${CODE_DIFF}
\`\`\`

Write your complete code review in markdown format."

claude -p "$REVIEW_PROMPT" --output-format text 2>&1 | tee "$REVIEW_FILE"

echo ""
echo "Review written to: $REVIEW_FILE"
```

Replace the placeholders with actual values before executing.

#### Step 3c: Review Prompt Template

Both review paths use this core prompt structure. When generating the script, substitute the actual values:

```
You are a senior code reviewer. Review the following code changes.

## Context
Work description: {WORK_DESCRIPTION}
{PLAN_CONTEXT - include "Plan reference: <path>" if plan exists}

## Instructions
1. Review the diff for correctness, security, performance, and maintainability.
2. Flag any bugs, logic errors, or potential issues.
3. Note any deviations from the plan (if a plan was provided).
4. Suggest improvements where appropriate.
5. Write your full review as a markdown document.
6. Be specific: reference file names and line numbers from the diff.

## Code Changes
```diff
{CODE_DIFF}
```

Write your complete code review in markdown format.
All review artifacts must be written to the planning/code-review/ folder.
```

### Step 4: Display Results

After the review script completes:

1. Read the review file that was written to `planning/code-review/`.
2. Display the full review contents to the user.
3. Inform the user where the review artifact is saved.
4. Ask if they want to take any action based on the review findings.

## Notes

- The cross-review pattern leverages the different strengths and blind spots of each AI tool.
- Review artifacts in `planning/code-review/` serve as a persistent record.
- If either tool fails during execution, report the error output to the user.
- This skill pairs well with the `ace-fca-workflow` skill for reviewed implementation phases.
