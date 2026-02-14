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
Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ .claude-plugin/
Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ marketplace.json          # Claude Code marketplace
Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ .github/
Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ plugin/
Ã¢â€â€š       Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ marketplace.json      # Copilot CLI marketplace
Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ README.md
Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ index.json                    # Auto-generated plugin index
Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ scripts/
Ã¢â€â€š   Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ build-plugin.ps1          # Cross-platform build
Ã¢â€â€š   Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ validate-skills.ps1       # Agent Skills spec validator
Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ generate-index.ps1        # Legacy (calls build-plugin)
Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ claude-code/
    Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ plugins/
        Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ ai-plugins-and-skills-config-sync/
        Ã¢â€â€š   Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ plugin.yaml               # Source of truth
        Ã¢â€â€š   Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ .claude-plugin/
        Ã¢â€â€š   Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ plugin.json            # Generated
        Ã¢â€â€š   Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ commands/
        Ã¢â€â€š   Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ config-sync.md
        Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ skills/
        Ã¢â€â€š       Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ config-sync/
        Ã¢â€â€š           Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ SKILL.md
        Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ ai-plugins-and-skills/
            Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ plugin.yaml               # Source of truth
            Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ .claude-plugin/
            Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ plugin.json            # Generated
            Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ commands/
            Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ ai-plugins-and-skills-init.md
            Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ skills/
                Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ ace-fca-workflow/
                Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ SKILL.md
                Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ angular-architect/
                Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ SKILL.md
                Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ angular-expert/
                Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ SKILL.md
                Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ azure-infra-engineer/
                Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ SKILL.md
                Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ codebase-mapper/
                Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ SKILL.md
                Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ ai-plugins-and-skills-init/
                Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ SKILL.md
                Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ csharp-quality-expert/
                Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ SKILL.md
                Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ python-pro/
                Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ SKILL.md
                Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ react-specialist/
                Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ SKILL.md
                Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ sql-pro/
                Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ SKILL.md
                Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ typescript-pro/
                    Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ SKILL.md
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
