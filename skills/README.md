# Skills

This folder holds the source-of-truth skills for this repo. If you're looking for the categorized skill catalog (browse by category, click into individual skills), that's at [the repo root README](../README.md#skills). If you want to understand what a "skill" *is* and where to find the per-harness installation guides, you're in the right place.

---

## What is a skill?

A **skill** is a piece of situational knowledge that you give to an AI agent. It's not a model, not a plugin, not a chat prompt — it's a discrete chunk of expertise the agent loads only when the situation calls for it. Skills usually take one of three shapes:

- **Repeatable processes** — "here's how we run a code review on this team," "here's our RED-GREEN-REFACTOR loop," "here's how we draft an ADR."
- **Tools the agent can use** — a script the agent invokes when needed (`sql-query`, `bulk-rename`, `azure-servicebus-operations`), with the SKILL.md explaining when to reach for it and how to read its output.
- **General knowledge about a subject** — coding standards for a language, architectural patterns for a stack, the operational quirks of a specific service.

The point is collaboration. You and the agent are working together; skills are the shared vocabulary that keeps both of you on the same page without you having to re-explain everything in every prompt. The agent loads the skill when its description matches what you're doing — no manual ceremony required in most harnesses.

Skills follow the open [**Agent Skills specification**](https://agentskills.io) — a single `SKILL.md` per skill, with YAML frontmatter naming the skill and describing when to activate it, followed by Markdown instructions for the agent. Because the format is portable, the skills in this repo work across Claude Code, Copilot CLI, and VS Code Copilot — same files, different harness wiring.

---

## Per-harness installation guides

| Harness | Guide | Notes |
|---|---|---|
| Claude Code | [README-CLAUDECODE.md](../documentation/README-CLAUDECODE.md) | Marketplace install with slash commands |
| Copilot CLI | [README-COPILOT-CLI.md](../documentation/README-COPILOT-CLI.md) | Plugin install via `/plugin install` |
| VS Code Copilot | [README-VSCODE-COPILOT.md](../documentation/README-VSCODE-COPILOT.md) | Agent mode via `chat.agentSkillsLocations` |

The skills themselves are identical across all three — what changes is how each harness discovers and loads them. Pick the guide that matches the tool you actually live in.

---

## Where the skills live

**Source of truth:** this folder. Each subdirectory is a self-contained skill — `SKILL.md` plus any companion files it needs (e.g., subagent templates like [plan-intake-review/plan-reviewer.md](plan-intake-review/plan-reviewer.md) and [code-intake-review/code-reviewer.md](code-intake-review/code-reviewer.md)).

For repo-wide structure, build scripts, and platform support matrix, see [documentation/ARCHITECTURE.md](../documentation/ARCHITECTURE.md).

For the workflows that *use* these skills (spec-driven coding, plan review, code review), see [documentation/workflows/](../documentation/workflows/README.md).
