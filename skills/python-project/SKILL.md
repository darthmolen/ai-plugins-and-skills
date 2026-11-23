---
name: python-project
description: "Use when scaffolding a new Python project or auditing an existing one against the python-architect standard. `new --kind script|package` writes pyproject.toml (PEP 735 dependency groups, ruff, pytest pythonpath, requires-python), tests, .gitignore and an Azure DevOps pipeline, then runs uv lock; it is a dry run by default and refuses to overwrite. `check` reports bare requirements*.txt, setup.py or setup.cfg, sys.path edits in tests, and a missing uv.lock, requires-python, dependency-groups or ruff config, with file and line, and exits 0 clean, 1 findings, 2 could not run."
metadata:
  category: tools
---

# Python project: scaffold or audit

`scripts/python_project.py` applies the `python-architect` standard. This file tells you how to
drive it. The script owns the files and the rules. You own the project name, the kind, reading the
dry run, and fixing what `check` reports.

The script needs Python 3.11 or later and nothing outside the standard library. Run it with `-I`
so a file in the current directory cannot shadow a standard-library module.

```powershell
# Set once per session — works whether installed via plugin or as a user skill
$pp = if ($env:CLAUDE_PLUGIN_ROOT) {
  "$env:CLAUDE_PLUGIN_ROOT\skills\python-project\scripts"
} else {
  "$HOME\.claude\skills\python-project\scripts"
}
```

```bash
pp="${CLAUDE_PLUGIN_ROOT:-$HOME/.claude}/skills/python-project/scripts"
```

## `check`: audit an existing project

```powershell
python -I "$pp\python_project.py" check --path src/billing-export
```

```text
REQUIREMENTS-TXT requirements-dev.txt move these into pyproject.toml
NO-PYPROJECT pyproject.toml missing
NO-UV-LOCK uv.lock missing; run `uv lock` and commit it
SYS-PATH-EDIT tests/test_x.py:2 use [tool.pytest.ini_options] pythonpath instead
4 finding(s)
```

| Rule | Finding | Fix |
|---|---|---|
| `REQUIREMENTS-TXT` | A `requirements*.txt` at the project root | Move runtime packages to `[project] dependencies` and dev tools to `[dependency-groups] dev`, then delete the file |
| `SETUP-FILE` | `setup.py` or `setup.cfg` | Move the metadata into `[project]` |
| `NO-PYPROJECT` | No `pyproject.toml` | Run `new --apply --no-lock` in an empty scratch folder, and copy its `pyproject.toml` in |
| `NO-REQUIRES-PYTHON` | `[project]` has no `requires-python` | Set it to the lowest runtime: `">=3.11"` for Fabric Runtime 1.3 |
| `NO-DEPENDENCY-GROUPS` | No `[dependency-groups]` table | Add `dev = ["pytest>=8", "ruff>=0.7"]` |
| `NO-RUFF-CONFIG` | No `[tool.ruff]` table | Copy it from the template, with `target-version` matching `requires-python` |
| `NO-UV-LOCK` | No `uv.lock` | Run `uv lock` and commit the file |
| `SYS-PATH-EDIT` | `sys.path.insert` or `sys.path.append` in a `.py` file under `tests/` | Delete the line, and add the folder to `[tool.pytest.ini_options] pythonpath` |

`check` reads only `.py` files for `SYS-PATH-EDIT`, so notebooks (`*.ipynb`) and Fabric item
folders are skipped. Moving code from a notebook into a module is covered by `python-data`.

Fix the findings, then run `check` again until it exits 0.

| Exit | Means |
|---|---|
| 0 | No findings |
| 1 | Findings, listed one per line |
| 2 | Could not run: the path is not a directory, or `pyproject.toml` does not parse |

## `new`: scaffold a project

Ask the user for the project name and the kind. Do not guess the kind from the name.

| Kind | For | Layout |
|---|---|---|
| `script` | Jobs, CLIs and scripts that are run, not installed | `tools/<module>.py` + `tests/` |
| `package` | A package built and published to a feed | `src/<module>/__init__.py` + `tests/` |

```powershell
# Dry run: lists what it would write, and any file that already exists
python -I "$pp\python_project.py" new --kind script --name billing-export --path src/billing-export

# Write the files, then run `uv lock`
python -I "$pp\python_project.py" new --kind script --name billing-export --path src/billing-export --apply
```

| Flag | Meaning |
|---|---|
| `--kind` | `script` or `package`; required |
| `--name` | The project name, which starts with a letter and may contain letters, digits, `.`, `_` and `-`. The module name is the name with `.` and `-` turned into `_`, in lower case |
| `--path` | The project root; default the current directory |
| `--python` | The lowest runtime, such as `3.12`. It sets `requires-python`, ruff's `target-version` and the pipeline's Python. Default `3.11`, the Fabric Runtime 1.3 version |
| `--apply` | Write the files. Without it, nothing is written |
| `--no-lock` | Skip `uv lock` after writing |

If any target file already exists, `new` writes nothing, lists each one as `COLLISION`, and exits 1.
Without uv on the path it writes the files and says to run `uv lock`.

After `--apply`, run these in the project folder, and expect each to pass:

```powershell
uv sync
uv run ruff check .
uv run ruff format --check .
uv run pytest
python -I "$pp\python_project.py" check
```

Then commit everything, `uv.lock` included.

## The templates are the standard

`scripts/templates/` holds the worked examples `python-architect` cites:

| Template | Becomes |
|---|---|
| `pyproject.script.toml` | `pyproject.toml` for a script project |
| `pyproject.package.toml` | `pyproject.toml` for a package |
| `azure-pipelines.python.yml` | `azure-pipelines.yml`, with a `useUv` parameter for the pip fallback. On GitHub, delete it and run the same steps in a workflow (`python-architect`, The CI pipeline) |
| `gitignore` | `.gitignore` |
| `tool.py.tmpl`, `test_tool.py.tmpl` | The script module, with the exit-code wrapper, and its test |
| `package_init.py.tmpl`, `test_package.py.tmpl` | The package `__init__.py` and its test |

Placeholders are `@name@`, `@module@`, `@python_version@` and `@target_version@`. They use `@`
because the pipeline template already uses Azure DevOps' `${{ }}` syntax. A placeholder with no value stops
the run with exit 2 rather than writing it into a file.

To change the standard, change the template and `python-architect` together.

## Tests

```bash
python -m pytest "$pp" -q
```

`test_python_project.py` covers every `check` rule and every `new` option. Two of its tests run the
generated project's own test suite in a subprocess. Every rule has a seeded mutant that the suite
kills (`mutation-harness`).
