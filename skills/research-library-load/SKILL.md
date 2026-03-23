---
name: research-library-load
description: >
  Manage external research codebases in a gitignored research/ directory. Auto-invoked when
  the user mentions checking out external code for reference, reviewing another repo's
  implementation, needing to study how a library or framework works internally, or when
  any task requires cloning or examining an external codebase. Handles cloning repos into
  research/, updating the research manifest, and ensuring research/ is properly gitignored.
user-invocable: false
allowed-tools: Read Write Edit Bash Glob Grep
---

# Research Library Management

The `research/` directory at the repo root is a **gitignored folder** for storing external codebases checked out for reference. It is never committed except for its manifest file.

## Rules

1. **Always gitignored**: `research/` must be in `.gitignore` with `!research/.manifest.yaml` to keep the manifest tracked.
2. **Read-only reference**: Never modify files inside `research/<repo>/`. They exist for reading and understanding only.
3. **Manifest is source of truth**: Every repo in `research/` must have a corresponding entry in `research/.manifest.yaml`. The SessionStart hook uses this to auto-restore missing repos.

## Manifest Location

The manifest lives at `research/.manifest.yaml` in the repo root. It is checked into git (via `.gitignore` negation) so that the SessionStart hook can restore research repos after teleport, on Claude Code web, or in fresh clones.

## Manifest Format

```yaml
repos:
  - name: my-library        # Directory name under research/
    url: https://github.com/org/my-library.git
    ref: main                # Optional: branch, tag, or commit SHA
    sparse:                  # Optional: sparse-checkout paths (for large repos)
      - src/core
      - docs/api
```

### Fields

| Field | Required | Description |
|-------|----------|-------------|
| `name` | Yes | Directory name created under `research/` |
| `url` | Yes | Git clone URL |
| `ref` | No | Branch, tag, or commit to checkout. Defaults to HEAD |
| `sparse` | No | List of paths for sparse checkout. Omit to clone the full repo |

## How to Add a Research Repo

When the user asks to check out an external codebase for reference:

1. **Clone it**:
   ```bash
   git clone <url> research/<repo-name>
   ```
   Or with sparse checkout for large repos:
   ```bash
   git clone --filter=blob:none --sparse <url> research/<repo-name>
   cd research/<repo-name>
   git sparse-checkout set <path1> <path2>
   ```

2. **Checkout a specific ref** (if needed):
   ```bash
   cd research/<repo-name> && git checkout <ref>
   ```

3. **Update the manifest** by adding an entry to `research/.manifest.yaml`:
   ```yaml
   - name: repo-name
     url: https://github.com/org/repo-name.git
     ref: v2.1.0
   ```

4. **Verify gitignore**: Confirm `research/` is in `.gitignore` with the `!research/.manifest.yaml` exception.

## How to Remove a Research Repo

1. Delete the directory: `rm -rf research/<repo-name>`
2. Remove the entry from `research/.manifest.yaml`

## How to Use Research Repos

- Read files in `research/<name>/` to understand patterns, APIs, implementations, or architecture
- Use Grep and Glob to search across research repos
- Reference specific files when explaining approaches or patterns to the user
- Do not copy code verbatim without noting the source and license

## Auto-Restore Behavior

A `SessionStart` hook runs `scripts/restore-research.sh` which:
1. Reads `research/.manifest.yaml`
2. Checks which repos are missing from `research/`
3. Clones any missing repos (with sparse checkout if configured)
4. Prints a status summary into the session context

This means research repos survive teleport, Claude Code web sessions, and fresh environment setups automatically.

### Windows (PowerShell) Support

A PowerShell equivalent is available at `scripts/restore-research.ps1`. The default hook uses `bash`, which is available on most platforms (including Windows via Git Bash). For pure-PowerShell environments, update the hook command in `.claude/settings.json`:

```json
"command": "pwsh -NoProfile -File skills/research-library-load/scripts/restore-research.ps1"
```
