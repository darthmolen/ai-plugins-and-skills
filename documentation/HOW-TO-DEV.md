# Developer Guide

How to develop and contribute skills for AI-Plugins-And-Skills.

## Prerequisites

- **Python 3.10+** with `pyyaml` (`pip install pyyaml`)
- **Git** with access to github [ai-plugins-and-skills](https://github.com/darthmolen/ai-plugins-and-skills) repository
- Familiarity with the [Agent Skills specification](https://agentskills.io/specification)

## One-time setup

Point this clone's git hooks at the repo-tracked hook directory so the pre-commit validator runs automatically:

```bash
git config core.hooksPath scripts/git-hooks
```

## Development Workflow

1. Create or edit a `SKILL.md` under `skills/<skill-name>/`
2. Run `python3 scripts/build_index.py` to validate and regenerate `index.json`, the README skills table, and the ARCHITECTURE directory tree
3. Commit -- the pre-commit hook re-runs the script and re-stages the regenerated files if any `SKILL.md` changed

## SKILL.md Format

Each skill is a directory containing a `SKILL.md` file. The file uses YAML frontmatter followed by Markdown instructions:

```yaml
---
name: my-skill-name          # Required. Lowercase, hyphens only. Max 64 chars.
description: >               # Required. Max 1024 chars.
  What this skill does and when to activate it.
allowed-tools: Read Write Edit Bash Glob Grep  # Optional. Tools the skill can use.
---

# Skill Instructions

Detailed instructions, patterns, rules, and examples for the AI agent.
```

The `name` must match the directory name. The `description` determines when AI agents activate the skill. See the full [specification](https://agentskills.io/specification) for all fields.

## Build System

### Scripts

- **`scripts/build_index.py`** — validates every `SKILL.md` against the Agent Skills spec and regenerates `index.json`, the README skills table, and the ARCHITECTURE directory tree. Exits 1 on validation failure.
- **`scripts/git-hooks/pre-commit`** — runs `build_index.py` when a staged change touches `skills/*/SKILL.md` or `scripts/build_index.py`, then re-stages the regenerated artifacts. Activated per clone via `git config core.hooksPath scripts/git-hooks` (see [One-time setup](#one-time-setup)).

### Generated files

Do not edit these directly — they are rewritten on every run:

- `index.json` — whole file
- `README.md` — between `<!-- BEGIN SKILLS -->` and `<!-- END SKILLS -->`
- `documentation/ARCHITECTURE.md` — code block under `## Directory Structure`

## Adding a New Skill

1. Create a directory: `skills/<skill-name>/`
2. Create `SKILL.md` with the required frontmatter (`name`, `description`)
3. Run `python3 scripts/build_index.py`
4. Verify the skill appears in `index.json` and the README skills table
5. Commit -- the hook will re-run the build if you forgot step 3

## VS Code Copilot Skill Discovery

VS Code Copilot discovers skills from several locations:

- `.github/skills/<skill-name>/` (project-level, primary)
- `.claude/skills/<skill-name>/` (project-level, backwards compatible)
- `~/.copilot/skills/<skill-name>/` (user-level, available in all projects)
- Custom paths via `chat.agentSkillsLocations` setting

This repo's skills live under `skills/` and are surfaced to VS Code Copilot via the `chat.agentSkillsLocations` setting. See [README-VSCODE-COPILOT.md](README-VSCODE-COPILOT.md) for installation instructions.
