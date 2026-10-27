---
name: codebase-mapper
description: "Generate a deterministic architecture map of Python, C#, and TypeScript codebases using AST parsing. Writes the map to documentation/CODEBASE-MAP.md and a short pointer to it in CLAUDE.md, so the map is read on demand instead of loaded every session. Use when mapping a codebase, generating architecture documentation, updating claude.md structure, or onboarding to a new project."
allowed-tools: Read Write Edit Bash Glob Grep
metadata:
  category: tools
  order: 1
---
# Codebase Structure Mapper

Deterministically parse Python, C#, and TypeScript codebases to extract structural maps (namespaces, classes, methods, inheritance, dependencies). The map is written to its own file; CLAUDE.md gets only a short pointer to it.

---

## Why This Skill?

- **Zero AI tokens for parsing** — Pure script execution using AST parsers
- **Deterministic & repeatable** — Same code = same output, version-controllable
- **Token-efficient output** — Compact format optimized for LLM consumption
- **No per-session token tax** — CLAUDE.md is loaded into every session; the map is only read when someone needs to find where something lives
- **Marker-based updates** — Preserves hand-written CLAUDE.md sections

---

## Workflow

| Phase | Name | Description |
|-------|------|-------------|
| 1 | DETECT | Auto-detect project languages and entry points |
| 2 | PARSE | Run appropriate AST-based mapper scripts |
| 3 | MERGE | Write the map file; insert/update the pointer in CLAUDE.md with markers |
| 4 | REPORT | Display summary and both token counts |

---

## Phase 1: DETECT

### Identify Project Languages

Scan for language indicators:

| Language | Detection Files |
|----------|-----------------|
| Python | `*.py`, `pyproject.toml`, `requirements.txt`, `setup.py` |
| C# | `*.cs`, `*.csproj`, `*.sln` |
| TypeScript | `*.ts`, `*.tsx`, `tsconfig.json` |

```bash
# Count files by type
find . -name "*.py" -type f | wc -l
find . -name "*.cs" -type f | wc -l
find . -name "*.ts" -o -name "*.tsx" -type f | wc -l
```

### Identify Source Roots

Look for common source directory patterns:
- Python: `src/`, `app/`, `<project_name>/`
- C#: `src/`, solution folder structure from `.sln`
- TypeScript: `src/`, `lib/`, `app/`

---

## Phase 2: PARSE

Run the appropriate mapper scripts based on detected languages.

### Python Projects

```bash
python scripts/map_python.py --root ./src --output $TMP/map_python.md
```

**What it extracts:**
- Modules and packages
- Classes with base classes
- Public methods and their signatures
- Key decorators (`@app.route`, `@property`, `@staticmethod`)
- Module-level functions
- Import dependencies (FastAPI, Django, Flask, SQLAlchemy detection)

### C# Projects

```bash
python scripts/map_csharp.py --root ./src --output $TMP/map_csharp.md
```

**What it extracts:**
- Namespaces
- Classes, interfaces, enums, records
- Inheritance and interface implementations
- Public methods and properties
- Key attributes (`[ApiController]`, `[HttpGet]`, `[Authorize]`)
- Using statements for dependency detection

### TypeScript Projects

```bash
python scripts/map_typescript.py --root ./src --output $TMP/map_typescript.md
```

**What it extracts:**
- Modules and exports
- Classes with inheritance
- Interfaces and types
- Functions (exported and module-level)
- React components (function and class-based)
- Key decorators (Angular `@Component`, `@Injectable`, etc.)

---

## Phase 3: MERGE

Use the update script to write the map file and point at it from CLAUDE.md:

```bash
python scripts/update_claude_md.py \
  --claude-md ./CLAUDE.md \
  --maps $TMP/map_python.md $TMP/map_csharp.md $TMP/map_typescript.md
```

Use the session scratchpad (or any temp directory) for `$TMP`; the per-language maps are intermediates.

### Where the map goes

| Repo has | Map file |
|----------|----------|
| `documentation/` (with or without `docs/`) | `documentation/CODEBASE-MAP.md` |
| only `docs/` | `docs/CODEBASE-MAP.md` |
| neither | `documentation/CODEBASE-MAP.md` (folder created) |

Override with `--map-file <path>`. The repo root is the directory holding CLAUDE.md. Commit the map file — the output is deterministic, so diffs show structural change.

### Why a pointer, not the map

CLAUDE.md is loaded into every session, so everything in it is paid for whether or not it is needed. The map grows with the codebase; the pointer is ~50 tokens. Claude reads the map only when it needs to find where something lives.

**Never turn the pointer into an `@path` import.** CLAUDE.md imports are inlined at load time, which puts the whole map back into every session. The path is written in backticks on purpose.

### Marker System

The script uses markers to preserve hand-written sections:

```markdown
<!-- CODEBASE-MAP:AUTO-START -->
## Architecture Map

[Auto-generated content here]
<!-- CODEBASE-MAP:AUTO-END -->
```

- If markers exist: Replace content between them (an older inlined map is migrated to the pointer)
- If no markers: Append after `## Project Context` section
- Hand-written sections outside markers are preserved
- `--remove` deletes both the marker block and the map file

---

## Phase 4: REPORT

Display summary:

```
Codebase Map Generated
======================
Languages: Python, C#
Files scanned: 142
Classes mapped: 47
Methods mapped: 312

Map:     documentation/CODEBASE-MAP.md  (~450 tokens, read on demand)
Pointer: CLAUDE.md                      (~50 tokens, loaded every session)

Markers: <!-- CODEBASE-MAP:AUTO-START/END -->
Re-run this skill anytime to update.
```

---

## Output Format

**CLAUDE.md** gets only the pointer:

```markdown
<!-- CODEBASE-MAP:AUTO-START -->
## Architecture Map

Before searching for where a class, module, or file lives, read `documentation/CODEBASE-MAP.md`
(generated by codebase-mapper; its header shows the commit it was built from).
If it disagrees with the code, trust the code and use Grep/Glob.
<!-- CODEBASE-MAP:AUTO-END -->
```

**documentation/CODEBASE-MAP.md** holds the map, in a compact, scannable format optimized for LLM consumption:

```markdown
# Codebase Map

*Auto-generated by codebase-mapper on 2026-10-02 13:02 from commit `e91d48c`. Re-run the skill to refresh. If this disagrees with the code, the code wins.*

### Python: src/
- `api/` — FastAPI REST endpoints
  - `routes/users.py`: `UserRouter` → get_user(), create_user(), update_user()
  - `routes/orders.py`: `OrderRouter` → list_orders(), create_order()
- `models/` — SQLAlchemy ORM models
  - `user.py`: `User(Base)` — id, email, name, created_at
  - `order.py`: `Order(Base)` — id, user_id, total, status

### C#: src/MyApp/
- `MyApp.Api` — ASP.NET Core Web API
  - `Controllers/UserController.cs`: `UserController : ControllerBase` → Get(), Post(), Put()
- `MyApp.Core` — Domain models and interfaces
  - `Models/User.cs`: `User` — Id, Email, Name, CreatedAt
  - `Interfaces/IUserRepository.cs`: `IUserRepository` → GetByIdAsync(), CreateAsync()

### TypeScript: src/
- `components/` — React components
  - `UserCard.tsx`: `UserCard` (FC) — props: user, onSelect
  - `OrderList.tsx`: `OrderList` (FC) — props: orders, loading
- `services/` — API clients
  - `userService.ts`: fetchUser(), createUser(), updateUser()
```

### Limits

This is on-demand discovery by convention, not by mechanism. It depends on Claude following the pointer, and nothing forces it to. The map is also a snapshot: re-run this skill after structural changes. The commit SHA in the header shows how stale it is.

---

## Configuration Options

### Depth Control

Control output verbosity via `--depth` flag:

| Depth | Output |
|-------|--------|
| `classes` | Namespaces and classes only |
| `methods` | Classes + public methods (default) |
| `full` | Classes + methods + properties + private members |

```bash
python scripts/map_python.py --root ./src --depth classes
```

### Exclusions

Exclude directories via `--exclude`:

```bash
python scripts/map_python.py --root ./src --exclude tests,migrations,__pycache__
```

Default exclusions: `node_modules`, `bin`, `obj`, `.git`, `__pycache__`, `venv`, `.venv`

---

## Script Locations

Scripts are located in `scripts/` relative to this skill:

| Script | Purpose |
|--------|---------|
| `map_python.py` | Python AST-based structure extractor |
| `map_csharp.py` | C# structure extractor (regex-based) |
| `map_typescript.py` | TypeScript structure extractor |
| `update_claude_md.py` | Writes the map file and the CLAUDE.md pointer |
| `test_update_claude_md.py` | pytest suite for `update_claude_md.py` |
| `test_map_paths.py` | Mappers emit `/` separators and POSIX directory order on every OS |

All scripts use Python stdlib only (no external dependencies). The tests need pytest:
`python -m pytest scripts -q`.

---

## Troubleshooting

### "No source files found"
- Verify source root directory is correct
- Check that file extensions match expected patterns

### "CLAUDE.md not found"
- Run `/ai-plugins-and-skills-init` first to create CLAUDE.md
- Or specify path: `--claude-md ./path/to/CLAUDE.md`

### "Parse errors in file X"
- Script continues on parse errors, logs warning
- Check for syntax errors in source file
- Non-UTF8 files are skipped with warning
