# AI Plugins and Skills - Copilot CLI

GitHub Copilot CLI plugin installation and usage guide.

See [README.md](../README.md) for the full plugin and skill listing.

## Prerequisites

The skills expect the same tools on your machine in every harness: `git`, the .NET 10 SDK for the single-file C# scripts, pwsh 7 for the session and ticket scripts, and Python 3.10+ with `uv` for `python-project`. The full list, with what needs each one, is in the [Claude Code prerequisites](README-CLAUDECODE.md#prerequisites). The `superpowers` plugin row applies to Claude Code only.

## Installation

```bash
/plugin list
/plugin install darthmolen/ai-plugins-and-skills
```

## Usage

### Skills

Skills work the same way across platforms -- Copilot CLI reads the `SKILL.md` files and activates them based on context.

| Skill | Activates When |
|-------|---------------|
| `csharp-quality-developer` | Writing or reviewing C# code |
| `python-developer` | Writing Python application code |
| `plan-tier` | Starting a plan, or deciding how much detail it needs |
| `session-workflow` | Starting or ending an agent session in a repository other sessions may share |
| `codebase-mapper` | Mapping a codebase, generating architecture docs |
| `ace-fca-workflow` | Complex multi-file tasks in large codebases |

Skills that Claude Code invokes as slash commands, such as `ticket-start 22744 implement`, work in Copilot when you name the skill and give the same arguments in your prompt.

### Plugin Management

```bash
/plugin list                                   # List installed plugins
/plugin update ai-plugins-and-skills           # Re-fetch from original source
/plugin uninstall ai-plugins-and-skills
```

## Updating

For local path installations, pull the latest changes and update:

```bash
cd ai-plugins-and-skills && git pull
```

Copilot CLI re-scans plugin sources on `/plugin update`, so:

```bash
/plugin update ai-plugins-and-skills
```

For symlinked installs, `git pull` is all that's needed -- changes are picked up at next startup.

## Current Limitations

### No Slash Commands

Copilot doesn't support slash commands for plugins. The `python-project` command in `commands/` is Claude Code only; ask for the `python-project` skill with `check` or `new` instead.

### No Agents

The `wave-lead` and `wave-track` agents under `agents/` are Claude Code only. In Copilot, start the same roles from the skill: invoke `wave-workflow` with `lead` or `track <name>` as its argument, and it loads the same instructions.

### No Hooks

The `wave-session-title` hook in `hooks/` is Claude Code only. Name each wave session by hand, `wave-<n>-lead` or `wave-<n>-<track>`, so the lead can address it.

### Marketplace

Copilot CLI marketplace requires GitHub-hosted repos.
