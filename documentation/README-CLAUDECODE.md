# AI-Plugins-And-Skills - Claude Code

Claude Code plugin installation, configuration, and usage guide.

See [README.md](../README.md) for the full plugin and skill listing.

## Prerequisites

This is the one list of what the plugin expects on your machine. The other install guides link here rather than repeating it.

**Required**

| What | Why | Check |
| --- | --- | --- |
| Claude Code, signed in | The harness the plugin runs in | `claude --version` |
| `git` | Cloning this repository, and the worktrees that sessions and waves work in | `git --version` |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | Several skills ship single-file C# scripts run with `dotnet run file.cs` (`normalize-cs`, `plan-doc`, `mutation-harness`). File-based apps need .NET 10 | `dotnet --version` reports `10.x` |
| The `superpowers` plugin | The skills here build on its workflow skills (brainstorming, plans, verification) | `/plugin` lists it |

Install `superpowers` from inside Claude Code:

```
/plugin marketplace add obra/superpowers-marketplace
/plugin install superpowers@superpowers-marketplace
```

**Needed by some skills**

| What | Needed by | Check |
| --- | --- | --- |
| [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) (`pwsh`) | The session and ticket scripts (`session-workflow`, `ticket-start`, `ticket-ship`) and the `wave-session-title` hook. Windows PowerShell 5.1 is not enough | `pwsh --version` |
| Python 3.10 or later, with [`uv`](https://docs.astral.sh/uv/getting-started/installation/) | `python-project` (its script needs Python 3.11) and the projects it scaffolds, and the Python variants of the tool skills | `python --version`, `uv --version` |
| [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), logged in with `az login` | `ticket-start` and `ticket-ship` for Azure DevOps work items, and the Azure tool skills | `az account show` |
| [GitHub CLI](https://cli.github.com/), logged in with `gh auth login` | `ticket-start` and `ticket-ship` for GitHub issues, and `pr-create` on GitHub | `gh auth status` |

## Installation

### Step 1: Prerequisites

Work through the [prerequisites](#prerequisites) above first.

### Step 2: Add the marketplace

Use the GitHub `<owner>/<repo>` this repository is published at:

```bash
claude plugin marketplace add darthmolen/ai-plugins-and-skills
```

Or clone it and add the clone:

```bash
git clone https://github.com/darthmolen/ai-plugins-and-skills.git ai-plugins-and-skills
cd ai-plugins-and-skills
claude plugin marketplace add "./"
```

### Step 3: Install the plugin

```bash
claude plugin install ai-plugins-and-skills@ai-plugins-and-skills-ai-standards
```

Or use the install script from a clone, which runs steps 2 and 3:

```bash
./scripts/claude-code/install.sh      # Unix/macOS
scripts\claude-code\install.cmd       # Windows
```

### Step 4: Restart Claude Code

Restart Claude Code to activate the plugin.

## Usage

### Skills (Auto-Invoked)

Skills activate automatically based on context. When Claude Code detects you're working with a relevant language or pattern, the matching skill loads. A few examples:

| Skill | Activates When |
|-------|---------------|
| `csharp-quality-developer` | Writing or reviewing C# code |
| `python-architect` | Setting up a Python project: layout, tooling, dependencies |
| `python-developer` | Writing Python application code |
| `plan-tier` | Starting a plan, or deciding how much detail it needs |
| `session-workflow` | Starting or ending an agent session in a repository other sessions may share |
| `codebase-mapper` | Mapping a codebase, generating architecture docs |
| `ace-fca-workflow` | Complex multi-file tasks in large codebases |

Any skill can also be invoked by name, with arguments:

```
/ai-plugins-and-skills:ticket-start 22744 implement
/ai-plugins-and-skills:ticket-ship 22744
```

### Slash Commands

| Command | Description |
|---------|-------------|
| `/ai-plugins-and-skills:python-project check [path]` | Audit an existing Python project against the `python-architect` standard |
| `/ai-plugins-and-skills:python-project new [path]` | Scaffold a new Python project: a dry run first, written only after you confirm |

Commands live in `commands/` and are Claude Code only.

### Agents

The plugin ships two agents for [`wave-workflow`](../skills/wave-workflow/), which runs several plans in parallel under one lead session. Start each in its own terminal:

```bash
claude --agent ai-plugins-and-skills:wave-lead --name wave-<n>-lead
claude --agent ai-plugins-and-skills:wave-track --name wave-<n>-<track>   # then say "track <track>"
```

In a VS Code extension tab, send `/ai-plugins-and-skills:wave-workflow lead` (or `track <track>`) as the first prompt. The `wave-session-title` hook below names the session for you.

### Hooks

The plugin ships one hook, in `hooks/hooks.json`:

| Hook | Event | What it does |
|------|-------|--------------|
| `wave-session-title` | `UserPromptSubmit` | When the prompt is exactly `/ai-plugins-and-skills:wave-workflow lead` or `/ai-plugins-and-skills:wave-workflow track <name>`, it reads the open wave's `planning/WAVE-README.md` and names the session `wave-<n>-lead` or `wave-<n>-<track>`, so the lead can message it without anyone renaming tabs |

The hook needs pwsh 7. A bash filter runs first, so pwsh starts only for a wave-workflow prompt. Any other prompt, no open wave, or a track the wave does not have produces no output, and the hook never blocks a prompt. Without pwsh, rename the session by hand.

### Sessions and tickets

When more than one agent works on your machine, each runs as a `session-workflow` session: a file in `~/.agent-sessions/` that records its subject and mode, plus locks on ports, local databases and containers it shares with the others. `ticket-start` and `ticket-ship` drive a session from a ticket, a GitHub issue or an Azure DevOps work item, to a draft pull request. See [SESSION-WORKFLOW.md](workflows/SESSION-WORKFLOW.md).

### C# Coding Standards

The `csharp-quality-developer` skill enforces:
- StyleCop rules (SA1028, SA1518, SA1101, etc.)
- File formatting (CRLF, trailing whitespace)
- Naming conventions (`this.` prefix, no underscore prefix)
- XML documentation standards
- LoggerMessage patterns

## Updating Plugins

After new changes are published:

```bash
claude plugin marketplace update ai-plugins-and-skills-ai-standards
claude plugin update ai-plugins-and-skills@ai-plugins-and-skills-ai-standards
```

Five language skills were renamed to role suffixes (`-architect`, `-developer`, `-engineer`) with no alias, so an instruction file or prompt that names an old skill needs the new name. [VOCABULARY.md](VOCABULARY.md#terms-that-used-to-mean-something-else) lists each old and new name. The old name for `plan-tier` still loads, as a deprecated shim.
