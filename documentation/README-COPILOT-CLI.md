# AI Plugins and Skills - Copilot CLI

GitHub Copilot CLI plugin installation and usage guide.

See [README.md](../README.md) for the full plugin and skill listing.

## Installation

```bash
/plugin list
/plugin install <<github-ai-plugins-and-skills>>
```

## Usage

### Skills

Skills work the same way across platforms -- Copilot CLI reads the `SKILL.md` files and activates them based on context.

| Skill | Activates When |
|-------|---------------|
| `csharp-quality-expert` | Writing or reviewing C# code |
| `codebase-mapper` | Mapping a codebase, generating architecture docs |
| `ace-fca-workflow` | Complex multi-file tasks in large codebases |

### Plugin Management

```bash
/plugin list                    # List installed plugins
/plugin update <<name-of-plugin-here>>   # Re-fetch from original source
/plugin uninstall <<name-of-plugin-here>>
```

## Updating

For local path installations, pull the latest changes and update:

```bash
cd ai-plugins-and-skills && git pull
```

Copilot CLI re-scans plugin sources on `/plugin update`, so:

```bash
/plugin update <<your-plugin-here>>
```

For symlinked installs, `git pull` is all that's needed -- changes are picked up at next startup.

## Current Limitations

### No Slash Commands

Copilot doesn't support slash commands for plugins

### Marketplace

Copilot CLI marketplace requires GitHub-hosted repos.