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
| Slash commands | Yes | No | No |
| Plugin system | Yes (marketplace) | Yes (marketplace) | No (skills only) |
| Auto-invocation | Yes | Yes | Yes |

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
└── skills/
    ├── csharp-quality-developer/
    │   └── SKILL.md
    ├── python-quality-developer/
    │   └── SKILL.md
    ├── python-quality-install/
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
    ├── azure-servicebus-operations/
    │   └── SKILL.md
    └── sql-query/
        └── SKILL.md
```

## Known Limitations

### Copilot CLI
- No slash command equivalent -- slash commands are Claude Code only
- No native Azure DevOps repository support -- `/plugin install owner/repo` only works with GitHub repos
- Marketplace requires GitHub-hosted repos for remote install

### VS Code Copilot
- No plugin or marketplace system -- skills are loaded individually via settings, not as bundles
- No slash commands -- describe what you want instead of using a command shortcut

### Authentication
- Copilot CLI authenticates via GitHub tokens only (`COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_TOKEN`, `gh` CLI, or OAuth device flow)
- No mechanism for Azure DevOps PATs or generic git credentials in the Copilot plugin system
- Claude Code uses its own marketplace system with local path support
