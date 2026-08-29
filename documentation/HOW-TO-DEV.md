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

The hook finds its own interpreter: it tries `python3`, `python`, `py -3` and `py`, and takes
the first one that can `import yaml`. The name is not the test, because on Windows `python3`
is often the Microsoft Store alias and the interpreter answering to a given name is not
always the one carrying PyYAML. If none of them can, the hook says so and names the fix
rather than failing as a validation error. To pin one explicitly -- in CI, or on a machine
with several Pythons:

```bash
PYTHON="py -3.12" git commit -m "..."
```

An explicit `PYTHON` is honoured strictly, with no fallback, so a wrong value fails loudly.

## Development Workflow

1. Create or edit a `SKILL.md` under `skills/<skill-name>/` (or under `plugins/<plugin>/skills/<skill-name>/` for an opt-in plugin -- see [Multiple plugins](#multiple-plugins))
2. Run `python scripts/build_index.py` to validate and regenerate `index.json`, the README skills table, and the ARCHITECTURE directory tree
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

- **`scripts/build_index.py`** — validates every `SKILL.md` against the Agent Skills spec and regenerates `index.json`, the README skills table, and the ARCHITECTURE directory tree. Scans every root listed in its `PLUGIN_ROOTS` constant, and rejects a skill name used by more than one plugin. Exits 1 on validation failure.
- **`scripts/git-hooks/pre-commit`** — runs `build_index.py` when a staged change touches `skills/*/SKILL.md`, `plugins/*/skills/*/SKILL.md`, or `scripts/build_index.py`, then re-stages the regenerated artifacts. Activated per clone via `git config core.hooksPath scripts/git-hooks` (see [One-time setup](#one-time-setup)).

### Generated files

Do not edit these directly — they are rewritten on every run:

- `index.json` — whole file
- `README.md` — between `<!-- BEGIN SKILLS -->` and `<!-- END SKILLS -->`
- `documentation/ARCHITECTURE.md` — code block under `## Directory Structure`

## Adding a New Skill

1. Create a directory: `skills/<skill-name>/`
2. Create `SKILL.md` with the required frontmatter (`name`, `description`, and `metadata.category`)
3. Make sure the README has a `<!-- SKILLS: <category> -->` / `<!-- END SKILLS: <category> -->` block for that category -- the build fails on a category with no block
4. Run `python3 scripts/build_index.py`
5. Verify the skill appears in `index.json` and the README skills table
6. Commit -- the hook will re-run the build if you forgot step 4

## Multiple plugins

This repo publishes more than one plugin from a single marketplace:

| Plugin | Skills live in | Installed |
|---|---|---|
| `ai-plugins-and-skills` | `skills/` | Default |
| `curriculum` | `plugins/curriculum/skills/` | Opt-in |

Every installed skill's `description` sits in the model's context for the whole session whether it
fires or not. A skill family that is inert without a specific workspace should not charge that cost
to everyone, so it ships as its own plugin instead.

To add another opt-in plugin:

1. Create `plugins/<name>/.claude-plugin/plugin.json` and `plugins/<name>/skills/`
2. Add an entry to `.claude-plugin/marketplace.json` with `"source": "./plugins/<name>"`
3. Add `("<name>", REPO / "plugins" / "<name>" / "skills")` to `PLUGIN_ROOTS` in `scripts/build_index.py`
4. Give its skills their own `metadata.category` and add the matching README block
5. Run `claude plugin validate ./plugins/<name>` and `claude plugin validate .`

## VS Code Copilot Skill Discovery

VS Code Copilot discovers skills from several locations:

- `.github/skills/<skill-name>/` (project-level, primary)
- `.claude/skills/<skill-name>/` (project-level, backwards compatible)
- `~/.copilot/skills/<skill-name>/` (user-level, available in all projects)
- Custom paths via `chat.agentSkillsLocations` setting

This repo's skills live under `skills/` and are surfaced to VS Code Copilot via the `chat.agentSkillsLocations` setting. See [README-VSCODE-COPILOT.md](README-VSCODE-COPILOT.md) for installation instructions.
