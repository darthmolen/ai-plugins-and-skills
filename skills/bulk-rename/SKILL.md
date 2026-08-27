---
name: bulk-rename
description: Use when a refactor needs the same identifier rename applied across many files (type-cluster renames, magic-string vocabulary moves) — runs a single bash invocation that does longest-first, word-boundary safe replacements across all tracked files in scope, instead of issuing dozens of per-file Edit tool calls
metadata:
  category: tools
---

# Bulk Rename

Apply N word-boundary-safe identifier renames across many files in one Bash call.

**Use when:** You're about to issue more than ~5 Edit calls that all do the same find/replace, OR a single rename involves more than ~5 files. The rename of `ChatScopeKey -> ChatScope` (and four siblings) across 47 files was the motivating case — it took ~70 Edit calls and burned a lot of context. This skill collapses that to one Bash invocation.

**Do NOT use when:**
- The change is conditional ("rename in test files but not in docs"). Edit per file.
- The OLD name is a substring of unrelated code where word boundaries won't save you (e.g. inside string literals or filenames). Edit per file.
- The rename is one-off in one file. Just use Edit.

## How

The skill ships a script at `${CLAUDE_PLUGIN_ROOT:-~/.claude/skills}/bulk-rename/rename.sh`. Two-phase usage:

### Phase 1 — Plan and dry-run

Build the pair list. Each pair is `OLD NEW`. The script processes them **longest-OLD first** so prefix collisions resolve correctly (e.g. `ChatScopeKey -> ChatScope` runs BEFORE `ChatScope -> SomethingElse`, but in practice you'd never batch those together — see "Transitive renames" below).

```bash
bash ~/.claude/skills/bulk-rename/rename.sh --scope '*.cs' \
  ChatScopeKey ChatScope \
  IChatScopeKey IChatScopeResolver \
  DefaultChatScopeKey StateBagChatScopeResolver \
  ChatScopeKeys ChatScopeStateBagKeys \
  AgentChatScopeExtractor AgentOptionsChatScopeResolver
```

Default mode is `--dry-run`: it prints a per-pair match count and total, then stops without modifying anything. Read the counts before applying — if a pair shows 0 matches, your name is wrong; if a pair shows 5,000 matches, your scope is too wide.

For larger plans, drop the pairs in a TSV file and pass it via `--pairs-from`:

```
ChatScopeKey	ChatScope
IChatScopeKey	IChatScopeResolver
DefaultChatScopeKey	StateBagChatScopeResolver
ChatScopeKeys	ChatScopeStateBagKeys
AgentChatScopeExtractor	AgentOptionsChatScopeResolver
```

```bash
bash ~/.claude/skills/bulk-rename/rename.sh --scope '*.cs' --pairs-from rename-pairs.tsv
```

### Phase 2 — Apply

Add `--apply`, or use `--commit "message"` to apply + commit in one shot:

```bash
bash ~/.claude/skills/bulk-rename/rename.sh --scope '*.cs' \
  --commit 'refactor: rename ChatScope* type cluster' \
  --pairs-from rename-pairs.tsv
```

The commit form stages only files the script actually modified — pre-existing dirty state in the worktree is left alone.

## Safety rules

The script does NOT replace verifying-before-applying. It surfaces match counts in dry-run; you still have to read them.

| Rule | Why |
|---|---|
| **Always dry-run first.** | The match-count table is your sanity check. A scope-too-wide bug or a typo in OLD shows up here before any file changes. |
| **Don't batch transitive renames** (`A→B` and `B→C` in one call). | The script warns when `NEW[i] == OLD[j]` but does not refuse — the second pass would clobber the first. Split into two invocations. |
| **Word boundaries are on by default.** | Disable with `--no-word-boundary` only for free-form text (strings, comments, doc files). Identifiers must keep boundaries — otherwise `Foo` matches inside `Foobar`. |
| **File paths are NOT renamed.** | The script only edits file *contents*. For path renames, run `git mv` first, THEN run this script to fix references inside the renamed files (and the rest of the tree). |
| **Untracked files are skipped.** | Operates on `git ls-files` only. If you want a new file in scope, `git add -N` it first. |
| **Binary files are skipped.** | The `file(1)` text/empty filter excludes images, dlls, etc. |

## When to fall back to Edit

- The replacement differs by file (e.g. you need different new names in test vs. prod). The script is one-rule-fits-all.
- You also need to update structural code around each occurrence (rename a parameter AND change its position in the signature). The script only does string substitution.
- The OLD name appears in some places you want to rename and others you don't. There's no per-occurrence opt-out — adjust scope, or do those files manually.

## When to fall back to a Roslyn-based rename

For C# specifically, when:
- You need semantic awareness (only rename symbol references, not string-literal occurrences of the same word).
- The rename crosses generic-type-parameter shapes the regex can't see cleanly.
- You're shipping the rename as a library API change and want the IDE-level safety net.

For everything else, this skill is enough.

## Worked example: the case this was built for

The chat-history-3.0 ChatScope* rename touched 47 files with these five pairs:

| OLD | NEW |
|---|---|
| `AgentChatScopeExtractor` | `AgentOptionsChatScopeResolver` |
| `DefaultChatScopeKey` | `StateBagChatScopeResolver` |
| `IChatScopeKey` | `IChatScopeResolver` |
| `ChatScopeKeys` | `ChatScopeStateBagKeys` |
| `ChatScopeKey` | `ChatScope` |

(Note ordering — `ChatScopeKey` and `ChatScopeKeys` differ by one character. Without longest-first ordering, renaming `ChatScopeKey -> ChatScope` would corrupt every `ChatScopeKeys` occurrence. The script handles this; you just supply the pairs in any order.)

Plus one namespace move that the script ALSO handles since the namespace string `Abstractions.ChatScope` appears verbatim in `using` directives:

```bash
bash ~/.claude/skills/bulk-rename/rename.sh --scope '*.cs' \
  --commit 'refactor(chat-history-3.0): rename ChatScope* type cluster' \
  AgentChatScopeExtractor AgentOptionsChatScopeResolver \
  DefaultChatScopeKey StateBagChatScopeResolver \
  IChatScopeKey IChatScopeResolver \
  ChatScopeKeys ChatScopeStateBagKeys \
  ChatScopeKey ChatScope \
  Abstractions.ChatScope Abstractions.ChatScopes
```

Caveat for the namespace pair: it contains a `.`, so word boundaries treat the dot as a word break — perl's `\b` anchors fire correctly on both sides. Verify with the dry-run match count before applying.

The path renames (`ChatScope/` → `ChatScopes/`) still need `git mv` separately. Workflow:

```bash
# 1. Move the files (path-only, no content changes yet).
git mv server-agent/src/.../ChatScope server-agent/src/.../ChatScopes
git mv server-agent/tests/.../ChatScope server-agent/tests/.../ChatScopes

# 2. Bulk-rename the references inside (and across) those files.
bash ~/.claude/skills/bulk-rename/rename.sh --scope '*.cs' \
  --commit 'refactor: rename ChatScope* type cluster' \
  --pairs-from rename-pairs.tsv

# 3. Verify.
dotnet build
dotnet test
```

## Do not

- Skip the dry-run. The match-count table is the cheapest sanity check available.
- Use `--no-word-boundary` on identifier renames. You will rename `Foo` inside `Foobar` and `MyFoo`.
- Batch transitive renames in one call. The warning is loud; if you ignore it, you'll clobber the first pass.
- Run on a dirty worktree without checking what's already staged. The `--commit` form will sweep up pre-existing changes if they touch the same files.
