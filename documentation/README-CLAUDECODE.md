# AI-Plugins-And-Skills - Claude Code

Claude Code plugin installation, configuration, and usage guide.

See [README.md](../README.md) for the full plugin and skill listing.

## Installation

### Step 1: Install Prerequisite Plugins

Install these plugins first from superpowers-marketplace:

```bash
/plugin install superpowers@superpowers-marketplace
/plugin install double-shot-latte@superpowers-marketplace
```

### Step 2: Add the Github Marketplace

```bash
# Clone this repository
git clone <<add-ai-plugin-and-skills-marketplace>>

# Register as a local marketplace
claude plugin marketplace add "./<<>>" # No longer valid. this is on a public repo
```

### Step 3: Install Plugins

```bash
claude plugin install ai-plugin-and-skills-markeplace@<<name-of-plugin>>
```

Or use the install script:

```bash
cd ai-plugin-and-skills
./scripts/claude-code/install.sh      # Unix/macOS
scripts\claude-code\install.cmd       # Windows
```

### Step 4: Restart Claude Code

Restart Claude Code to activate the plugins.

## Prerequisites

TBD

## Usage

### Skills (Auto-Invoked)

Skills activate automatically based on context. When Claude Code detects you're working with a relevant language or pattern, the matching skill loads.

| Skill | Activates When |
|-------|---------------|
| `csharp-quality-expert` | Writing or reviewing C# code |
ESLint |
| `codebase-mapper` | Mapping a codebase, generating architecture docs |
| `ace-fca-workflow` | Complex multi-file tasks in large codebases |

### Slash Commands

| Command | Description |
|---------|-------------|
| N/A | N/A |

### C# Coding Standards

The `csharp-quality-expert` skill enforces:
- StyleCop rules (SA1028, SA1518, SA1101, etc.)
- File formatting (CRLF, trailing whitespace)
- Naming conventions (`this.` prefix, no underscore prefix)
- XML documentation standards
- LoggerMessage patterns

## Updating Plugins

After pulling new changes from the repository:

```bash
claude plugin update ai-plugins-and-skills-marketplace@ai-plugins-and-skills-ai-standards
claude plugin update ai-plugins-and-skills-marketplace@ai-plugins-and-skills-ai-standards
```

