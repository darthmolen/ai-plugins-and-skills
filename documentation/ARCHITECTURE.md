# Architecture

Cross-platform design and structure for AI-Plugins-And-Skills plugins.

## Design Philosophy

One codebase serves multiple AI coding assistant platforms. Each plugin is authored once using a unified `plugin.yaml` manifest and the [Agent Skills](https://agentskills.io) `SKILL.md` format. Platform-specific outputs are generated at build time.

```
plugin.yaml (source of truth)
       |
  build-plugin.ps1
       |
  +----+----+----+
  |         |    |
  v         v    v
Claude   Copilot  VS Code
Code     CLI      Copilot
```

## Agent Skills Specification

Skills follow the [Agent Skills](https://agentskills.io) open specification, adopted by 26+ platforms including Claude Code, GitHub Copilot (CLI and VS Code), OpenAI Codex, Cursor, Windsurf, and others.

Each skill is a directory containing a `SKILL.md` file with YAML frontmatter:

```yaml
---
name: skill-name
description: "What the skill does and when to use it"
allowed-tools: Read Write Edit Bash Glob Grep
---
```

Skills are platform-agnostic. Any tool implementing the Agent Skills specification can discover and use them.

## Platform Support Matrix

| Feature | Claude Code | Copilot CLI | VS Code Copilot |
|---------|-------------|-------------|-----------------|
| Skills (SKILL.md) | Yes | Yes | Yes |
| Slash commands (`commands/*.md`) | Yes | No | No |
| Agents (`agents/*.md`) | Yes | No | No |
| Hooks (`hooks/hooks.json`) | Yes | No | No |
| Plugin system | Yes (marketplace) | Yes (marketplace) | No (skills only) |
| Auto-invocation | Yes | Yes | Yes |

Agents are Claude Code only. The two in `agents/` (`wave-lead`, `wave-track`) are thin wrappers that preload skills and point at `wave-workflow`'s `lead.md` / `track.md`; in Copilot the same roles start from the skill with `lead` or `track <name>` as its argument.

Commands are Claude Code only. `commands/python-project.md` gives `python-project` a slash command (`check` or `new`); in the other harnesses, ask for the skill by name.

Hooks are Claude Code only. `hooks/hooks.json` registers one `UserPromptSubmit` hook, `wave-session-title`, which names a wave session `wave-<n>-lead` or `wave-<n>-<track>` when its first prompt is `/ai-plugins-and-skills:wave-workflow lead` or `track <name>`. A bash filter runs first, so `pwsh` (7 or later) starts only for that prompt, and the hook never blocks one. In the other harnesses, name wave sessions by hand.

## How the workflow skills fit together

The planning and execution skills are layered. Each layer can be used without the ones above it.

```text
drivers ────────── ticket-start / ticket-ship, a plan you run yourself, a wave track
       │ supply the session's subject and mode
plan-workflow ──── the board: what the work is, its document, its status
       │
session-workflow ─ one session on one machine: worktree, session file, mode, local
       │           guidance, resource locks, stale detection, teardown at every stop
       │
wave-workflow ──── many sessions under a lead; each lead and track is one session

ending a plan ──── plan-workflow close, then the pull request (pr-create, Azure DevOps
                   or GitHub); ticket-ship adds the ticket's link
```

Specs sit beside this stack: `system-spec` owns the evergreen system spec for an area, a plan's feature-spec merges into it at the pull request, and `system-spec-derive` and `system-spec-backfill` write one for an area that has none. `plan-tier` sets how much plan and review a piece of work needs before `plan-workflow` takes it. [VOCABULARY.md](VOCABULARY.md) defines each term, and [workflows/](workflows/README.md) walks through each workflow.

### Skill names

A language or framework skill is named `<subject>-<role>`: `-architect` for project setup and design decisions, `-developer` for application code, and `-engineer` for infrastructure and platform work (`python-architect`, `python-developer`, `azure-infra-engineer`). Tool skills are named for what they do (`sql-query`), and workflow skills for the process they run (`session-workflow`). See [VOCABULARY.md](VOCABULARY.md#skill-names-architect-developer-engineer).

## Directory Structure

```
ai-plugins-and-skills/
├── LICENSE
├── README.md                     # Auto-generated skills table
├── index.json                    # Auto-generated skill index
├── .claude-plugin/
│   ├── plugin.json               # Claude Code plugin manifest
│   └── marketplace.json          # Claude Code marketplace manifest
├── documentation/
│   ├── ARCHITECTURE.md           # Auto-generated directory tree
│   ├── HOW-TO-DEV.md
│   ├── README-CLAUDECODE.md
│   ├── README-COPILOT-CLI.md
│   └── README-VSCODE-COPILOT.md
├── scripts/
│   ├── build_index.py            # Manual + pre-commit entry point
│   ├── claude-code/
│   │   ├── install.cmd           # Windows installer
│   │   └── install.sh            # Unix/macOS installer
│   └── git-hooks/
│       └── pre-commit            # Runs build_index.py
├── plugins/                      # Opt-in plugins, installed separately
│   └── curriculum/
│       ├── .claude-plugin/
│       │   └── plugin.json
│       └── skills/
│           ├── curriculum-classify/
│           │   └── SKILL.md
│           ├── curriculum-design/
│           │   └── SKILL.md
│           ├── curriculum-discover/
│           │   └── SKILL.md
│           ├── curriculum-extract/
│           │   └── SKILL.md
│           ├── curriculum-generate/
│           │   └── SKILL.md
│           ├── curriculum-govern/
│           │   └── SKILL.md
│           └── curriculum-orchestrate/
│               └── SKILL.md
└── skills/
    ├── angular-architect/
    │   └── SKILL.md
    ├── angular-developer/
    │   └── SKILL.md
    ├── azure-infra-engineer/
    │   └── SKILL.md
    ├── dotnet-secrets/
    │   └── SKILL.md
    ├── python-architect/
    │   └── SKILL.md
    ├── python-data/
    │   └── SKILL.md
    ├── python-developer/
    │   └── SKILL.md
    ├── react-developer/
    │   └── SKILL.md
    ├── sql-developer/
    │   └── SKILL.md
    ├── typescript-developer/
    │   └── SKILL.md
    ├── csharp-quality-developer/
    │   └── SKILL.md
    ├── python-quality-developer/
    │   └── SKILL.md
    ├── python-quality-install/
    │   └── SKILL.md
    ├── technical-writing/
    │   └── SKILL.md
    ├── plan-send-review/
    │   └── SKILL.md
    ├── plan-intake-review/
    │   └── SKILL.md
    ├── plan-receive-review/
    │   └── SKILL.md
    ├── code-send-review/
    │   └── SKILL.md
    ├── code-intake-review/
    │   └── SKILL.md
    ├── code-review-apply/
    │   └── SKILL.md
    ├── code-review-plan-create/
    │   └── SKILL.md
    ├── code-review-seed-slices/
    │   └── SKILL.md
    ├── code-review-plan-execute/
    │   └── SKILL.md
    ├── code-synthesize-reviews/
    │   └── SKILL.md
    ├── codebase-mapper/
    │   └── SKILL.md
    ├── ace-fca-workflow/
    │   └── SKILL.md
    ├── ado-pr/
    │   └── SKILL.md
    ├── azure-servicebus-operations/
    │   └── SKILL.md
    ├── bulk-rename/
    │   └── SKILL.md
    ├── mutation-harness/
    │   └── SKILL.md
    ├── pr-create/
    │   └── SKILL.md
    ├── psql-query/
    │   └── SKILL.md
    ├── python-project/
    │   └── SKILL.md
    ├── sql-query/
    │   └── SKILL.md
    ├── brown-bag/
    │   └── SKILL.md
    ├── plan-clear-blockers/
    │   └── SKILL.md
    ├── plan-record-review/
    │   └── SKILL.md
    ├── plan-sweep-blockers/
    │   └── SKILL.md
    ├── plan-tier/
    │   └── SKILL.md
    ├── plan-workflow/
    │   └── SKILL.md
    ├── plan-writing-syntax/
    │   └── SKILL.md
    ├── prompt-dotnet/
    │   └── SKILL.md
    ├── reminders-set/
    │   └── SKILL.md
    ├── session-workflow/
    │   └── SKILL.md
    ├── spec-tier/
    │   └── SKILL.md
    ├── system-spec/
    │   └── SKILL.md
    ├── system-spec-backfill/
    │   └── SKILL.md
    ├── system-spec-derive/
    │   └── SKILL.md
    ├── test-filter-development/
    │   └── SKILL.md
    ├── ticket-ship/
    │   └── SKILL.md
    ├── ticket-start/
    │   └── SKILL.md
    └── wave-workflow/
        └── SKILL.md
```

## Known Limitations

### Copilot CLI
- No slash command equivalent -- slash commands are Claude Code only
- No agents or hooks -- start wave roles from `wave-workflow` and name the sessions by hand
- No native Azure DevOps repository support -- `/plugin install owner/repo` only works with GitHub repos
- Marketplace requires GitHub-hosted repos for remote install

### VS Code Copilot
- No plugin or marketplace system -- skills are loaded individually via settings, not as bundles
- No slash commands -- describe what you want instead of using a command shortcut
- No agents or hooks -- start wave roles from `wave-workflow` and name the sessions by hand

### Authentication
- Copilot CLI authenticates via GitHub tokens only (`COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_TOKEN`, `gh` CLI, or OAuth device flow)
- No mechanism for Azure DevOps PATs or generic git credentials in the Copilot plugin system
- Claude Code uses its own marketplace system with local path support
