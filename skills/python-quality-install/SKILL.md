---
name: python-quality-install
description: Use when initializing a new Python project or adding quality tooling to an existing one - sets up ruff linter and formatter, pyright type checker, and pre-commit hooks with opinionated defaults that enforce python-quality-developer standards
metadata:
  category: language-helpers
---
# Python Quality Install

Set up ruff, pyright, and pre-commit in a Python project. Run as `/python-quality-install`.

## What It Does

1. Adds ruff + pyright config to `pyproject.toml`
2. Creates `pyrightconfig.json`
3. Creates `.pre-commit-config.yaml`
4. Adds dev dependencies
5. Installs and runs initial lint fix + format pass
6. Verifies zero violations

## Steps

### 1. Add ruff config to pyproject.toml

Append to `pyproject.toml` (merge if sections exist):

```toml
[tool.ruff]
target-version = "py310"  # match project's requires-python
line-length = 120
src = ["src"]  # adjust to project layout

[tool.ruff.lint]
select = [
    "E",    # pycodestyle errors
    "W",    # pycodestyle warnings
    "F",    # pyflakes
    "I",    # isort
    "UP",   # pyupgrade
    "B",    # flake8-bugbear
    "S",    # flake8-bandit (security)
    "T20",  # flake8-print
    "SIM",  # flake8-simplify
    "RUF",  # ruff-specific
]
ignore = [
    "E501",  # line-too-long — enforced by formatter, not linter
]

[tool.ruff.lint.per-file-ignores]
"tests/**" = ["S101", "S106", "S108", "S110", "RUF006"]
```

**Adjust `src` and per-file-ignores** for the project layout. If there's prototype/research code with `print()`, add `"T201"` ignore for those paths.

### 2. Create pyrightconfig.json

```json
{
    "include": ["src"],
    "pythonVersion": "3.10",
    "typeCheckingMode": "basic",
    "reportMissingTypeStubs": false,
    "reportUnknownMemberType": false,
    "reportUnknownParameterType": false,
    "reportUnknownVariableType": false,
    "reportUnknownArgumentType": false
}
```

Start with `basic` mode. Upgrade to `standard` once the codebase is clean. `strict` requires typing every SDK boundary which is impractical for most projects.

### 3. Create .pre-commit-config.yaml

```yaml
repos:
  - repo: https://github.com/astral-sh/ruff-pre-commit
    rev: v0.11.11  # use latest
    hooks:
      - id: ruff
        args: [--fix]
      - id: ruff-format
```

### 4. Add dev dependencies

Add to `[project.optional-dependencies]` in pyproject.toml:

```toml
[project.optional-dependencies]
dev = [
    # ... existing dev deps ...
    "ruff>=0.8",
    "pyright>=1.1",
    "pre-commit>=3.0",
]
```

### 5. Install and run

```bash
pip install -e ".[dev]"
pre-commit install        # may fail if core.hooksPath is set — that's OK
ruff check src/ tests/ --fix          # auto-fix what it can
ruff check src/ tests/ --fix --unsafe-fixes  # fix unused vars etc.
ruff format src/ tests/               # format everything
ruff check src/ tests/                # should be clean now
pyright src/                          # fix any type errors
```

### 6. Fix remaining violations

After auto-fix, common manual fixes needed:

| Violation | Fix |
|-----------|-----|
| `B904` raise-without-from | Add `from exc` to re-raises in except blocks |
| `S110` try-except-pass | Use `contextlib.suppress` or add `# noqa: S110` with comment |
| `F821` undefined-name | Missing import (often `Any`, `Path`) removed by auto-fix |
| `SIM102` collapsible-if | Combine nested `if` with `and` |
| `T201` print found | Replace with logging or add per-file ignore |

### 7. Verify

```bash
ruff check src/ tests/           # All checks passed!
ruff format --check src/ tests/  # N files already formatted
pyright src/                     # 0 errors, 0 warnings
pytest tests/ -v                 # All passing
```

## When to Use

- Starting a new Python project
- Inheriting a project with no quality tooling
- After `/python-quality-install`, the `python-quality-developer` skill enforces ongoing compliance

## Companion Skill

**REQUIRED:** `python-quality-developer` defines the coding standards that this tooling enforces. Install the tooling, follow the standards.
