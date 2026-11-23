# Developer Guide

How to develop and contribute skills for AI-Plugins-And-Skills.

## Prerequisites

- **Python 3.10+** with `pyyaml` (`pip install pyyaml`)
- **Git** with access to github [ai-plugins-and-skills](https://github.com/darthmolen/ai-plugins-and-skills) repository
- Familiarity with the [Agent Skills specification](https://agentskills.io/specification)

To run the skills' own scripts and tests you also need what their users need: the .NET 10 SDK for the single-file C# scripts, pwsh 7 for the session and ticket scripts and the hook, and Python 3.11+ for `python-project`. The full list is in the [Claude Code prerequisites](README-CLAUDECODE.md#prerequisites).

## One-time setup

Point this clone's git hooks at the repo-tracked hook directory so the pre-commit validator runs automatically:

```bash
git config core.hooksPath scripts/git-hooks
```

This repository was built by lifting skills out of a corporate repo, so the hook also refuses any
commit whose staged content carries a corporate identifier -- a client name, a production hostname,
a tenant GUID. The check runs before the index rebuild and reports the file and line. When it fires,
replace the identifier with a placeholder (`your-server`, `your-db`, `example-messages`) rather than
bypassing it; the bypass, `SKIP_SANITIZE_CHECK=1`, exists for edits to `.lift-audit/`, which
necessarily quotes the strings being banned. See `.lift-audit/AUDIT.md` for what leaked when this
was a manual grep instead.

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
- **`scripts/git-hooks/pre-commit`** — two checks. First the **sanitization gate**: staged content is scanned case-insensitively for corporate identifiers, and the commit is refused if any appear. Then, when a staged change touches `skills/*/SKILL.md`, `plugins/*/skills/*/SKILL.md`, or `scripts/build_index.py`, it runs `build_index.py` and re-stages the regenerated artifacts. Activated per clone via `git config core.hooksPath scripts/git-hooks` (see [One-time setup](#one-time-setup)).

### Generated files

Do not edit these directly — they are rewritten on every run:

- `index.json` — whole file
- `README.md` — between each `<!-- SKILLS: <category> -->` and `<!-- END SKILLS: <category> -->` pair (the flavor text above each block is hand-written)
- `documentation/ARCHITECTURE.md` — code block under `## Directory Structure`

## Adding a New Skill

1. Create a directory: `skills/<skill-name>/`
2. Create `SKILL.md` with the required frontmatter (`name`, `description`, and `metadata.category`). The `description` must stay within 1024 characters and contain no `<` or `>`, and the body within 500 lines; move longer material into a sibling reference `.md` and link it
3. Make sure the README has a `<!-- SKILLS: <category> -->` / `<!-- END SKILLS: <category> -->` block for that category -- the build fails on a category with no block
4. Run `python3 scripts/build_index.py`
5. Verify the skill appears in `index.json` and the README skills table
6. Commit -- the hook will re-run the build if you forgot step 4

When a change alters what the plugin ships, bump `version` in `.claude-plugin/plugin.json` and the matching entry in `.claude-plugin/marketplace.json`, so `claude plugin update` picks it up.

### Naming a skill

A domain skill is named `<subject>-<role>`, with one of three roles:

| Role | Covers | Examples |
|---|---|---|
| `-architect` | Project setup, tooling, layout, and design decisions made before or across the code | `angular-architect`, `python-architect` |
| `-developer` | Writing application code: idioms, patterns, and pitfalls | `angular-developer`, `python-developer`, `csharp-quality-developer` |
| `-engineer` | Infrastructure and platform work: provisioning, networking, identity, infrastructure as code, pipelines | `azure-infra-engineer` |

Not `-pro`, `-expert` or `-specialist`. The test for `-developer` versus `-engineer` is what the skill's output runs as: code that ships inside an application is `-developer`, and code that builds, deploys or provisions its environment is `-engineer`. Tool skills are named for what they do (`sql-query`, `bulk-rename`), and workflow skills for the process they run (`plan-workflow`, `session-workflow`). [VOCABULARY.md](VOCABULARY.md#skill-names-architect-developer-engineer) has the reasoning.

Renaming a skill breaks every prompt and instruction file that names it, so keep no alias that competes with the new name. When the old name must keep working for a while, ship a shim whose `description` says only that the name is deprecated and which skill to invoke, as `spec-tier` does for `plan-tier`.

### Scripts, tests, commands and hooks

A skill's scripts and their tests live inside the skill directory, because a skill is read from wherever it was installed. Run a skill's tests before committing a change to its scripts:

```bash
dotnet run skills/plan-workflow/scripts/plan-doc.cs -- --selftest
pwsh -NoProfile -File skills/session-workflow/test/Test-SessionScript.ps1
pwsh -NoProfile -File skills/ticket-start/test/Test-TicketScript.ps1
python -I -m pytest skills/python-project/scripts
```

Three Claude Code features live outside `skills/` and are not validated by `build_index.py`:

- `agents/*.md` -- agents that preload skills (`wave-lead`, `wave-track`)
- `commands/*.md` -- slash commands that hand off to a skill (`python-project`)
- `hooks/hooks.json` -- hooks the plugin registers, with their scripts beside it (`wave-session-title.ps1`). Keep a hook cheap: filter in the shell command first, so the interpreter starts only for the prompts it handles, and never block a prompt on failure

Each must wrap a skill that works on its own, because Copilot CLI and VS Code Copilot load the skill and nothing else.

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
