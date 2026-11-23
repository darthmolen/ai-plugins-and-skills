---
name: python-architect
description: "Python project setup and tooling standard: pyproject.toml, PEP 735 dependency groups, uv and uv.lock, ruff, pytest pythonpath, requires-python, flat versus src layout, CLI exit codes, and the CI pipeline (an Azure DevOps template, with the same steps for GitHub Actions). Use when starting a Python project, adding tests or linting to one, reviewing a requirements.txt or setup.py project, or answering 'how should this Python project be set up'. For writing the code, use python-developer (backend) or python-data (dataframes, Fabric). To scaffold or audit a project, use python-project. To add pyright and pre-commit hooks on top, use python-quality-install."
allowed-tools: Read Grep Glob Edit Write Bash
metadata:
  category: domain-skills
---

# Python Architect Skill

How a Python project is set up: the project file, the environment, the tools, and the layout.
Code inside the project belongs to `python-developer` (backend services) and `python-data`
(dataframes and Fabric notebooks).

The `python-project` skill applies this standard. `new` scaffolds a project and `check` audits one.
Its templates in `python-project/scripts/templates/` are the worked examples for everything below.
Read them rather than writing a `pyproject.toml` from memory.

## Auto-Detection

This skill activates when:
- A repository gains its first `*.py` file, `pyproject.toml` or `requirements*.txt`
- A project has `setup.py`, `setup.cfg` or a bare `requirements*.txt`
- Tests import the code under test with `sys.path.insert` or `sys.path.append`
- Someone asks how to install, lint, test or lock a Python project

## The standard

| Concern | Standard |
|---|---|
| Project file | One `pyproject.toml` per project. No `setup.py`, `setup.cfg` or bare `requirements*.txt` |
| Runtime dependencies | `[project] dependencies` |
| Dev dependencies | PEP 735 `[dependency-groups]`, for example `dev = ["pytest", "ruff"]` |
| Environment and lock | `uv`, with `uv.lock` committed. `pip install --group dev` (pip ≥ 25.1) must also work |
| Lint and format | `ruff`, configured under `[tool.ruff]` in `pyproject.toml` |
| Tests | pytest, with `[tool.pytest.ini_options] pythonpath` instead of `sys.path` edits |
| Python version | `requires-python` set to the lowest runtime the code must run on |
| Layout | Flat `tools/` + `tests/` for scripts. `src/` layout only for a package that is built and published. An existing layout is kept |
| CLI exit codes | 0 success, 1 problems found, 2 could not run |

## Why each rule

### One `pyproject.toml`

PEP 621 made `pyproject.toml` the place for project metadata, and pip, uv, ruff and pytest all
read it. A `requirements.txt` holds a list of packages with no Python version, no tool
configuration, and no way to say which packages are only for development. `setup.py` executes
code to describe the project, so a tool cannot read it without running it.

### Dependency groups for dev tools

PEP 735 `[dependency-groups]` holds packages the project needs to develop but not to run. They
are not published with a package, which an `[project.optional-dependencies]` extra would be.

```toml
[dependency-groups]
dev = ["pytest>=8", "ruff>=0.7"]
```

`uv sync` installs the `dev` group by default. With pip, `pip install --group dev` needs pip 25.1
or later.

### `uv`, and the lock committed

`uv` resolves, installs and locks in one tool, and reads the same `pyproject.toml` as pip. A
committed `uv.lock` means the CI agent and every developer install the same versions.

`uv` is recommended, and pip is the supported fallback. Nothing in the project may require uv:
`pip install -e . --group dev` followed by `pytest` must work on a machine without it. That keeps
Fabric environments and locked-down agents usable.

| Task | uv | pip fallback |
|---|---|---|
| Create the environment and install | `uv sync` | `python -m venv .venv`, activate it, then `pip install -e . --group dev` |
| Add a runtime dependency | `uv add polars` | Edit `[project] dependencies`, reinstall |
| Add a dev dependency | `uv add --dev pytest` | Edit `[dependency-groups] dev`, reinstall |
| Run the tests | `uv run pytest` | `pytest` |
| Update the lock | `uv lock` | not available; the lock is uv's |

### ruff for lint and format

ruff replaces flake8, isort, pyupgrade and black with one tool configured in `pyproject.toml`.
Set `target-version` to match `requires-python`, so ruff does not suggest syntax the runtime
cannot run.

```toml
[tool.ruff]
line-length = 100
target-version = "py311"

[tool.ruff.lint]
select = ["E", "F", "W", "I", "UP", "B"]
```

CI runs `ruff check .` and `ruff format --check .`.

### pytest `pythonpath`, not `sys.path`

A test that runs `sys.path.insert(0, "..")` before importing the code depends on the directory it
was started from, and hides the import path from every tool that reads the project. pytest's
`pythonpath` setting does the same job in one declared place:

```toml
[tool.pytest.ini_options]
pythonpath = ["tools"]
testpaths = ["tests"]
```

A `src/` layout package does not need `pythonpath`, because `pip install -e .` or `uv sync` puts
it on the path.

Testing discipline follows `test-filter-development`. `mutation-harness` runs a Python suite with
`--test-cmd "python -m pytest -q"`.

### `requires-python` from the runtime

Set `requires-python` to the lowest Python version the code must run on, and set ruff's
`target-version` to match.

| Runtime | Python | `requires-python` |
|---|---|---|
| Fabric Runtime 1.3 (GA) | 3.11 | `">=3.11"` |
| Fabric Runtime 2.0 (preview) | 3.13 | `">=3.13"`, only if nothing runs on 1.3 |
| CI agent or local only | the agent's version | the lowest version a developer or agent uses |

The Fabric versions were checked on 2026-10-28 against Microsoft Learn's
[Fabric runtime page](https://learn.microsoft.com/fabric/data-engineering/runtime). Check it again
when a runtime changes.

### Layout

| Project kind | Layout |
|---|---|
| Scripts, jobs and CLIs that are run, not installed | Flat: `tools/` (or the project's own folder name) beside `tests/` |
| A package that is built and published to a feed | `src/<package>/` beside `tests/` |
| An existing project | Keep its layout. Moving files to match this table costs review time and gains nothing |

A flat layout needs `pythonpath` in the pytest settings. Its template still declares a setuptools
`[build-system]` with empty `packages` and `py-modules`, only so the pip fallback
(`pip install -e . --group dev`) can install the dependencies, and sets `[tool.uv] package = false`
so uv does not build it. A `src/` layout declares a real build system and gets its import path
from installation.

```text
billing-export/               # flat: scripts
├── pyproject.toml
├── uv.lock
├── tools/
│   └── billing_export.py
└── tests/
    └── test_billing_export.py

fabric-utils/                 # src: a published package
├── pyproject.toml
├── uv.lock
├── src/
│   └── fabric_utils/
│       └── __init__.py
└── tests/
```

### CLI exit codes

A command-line tool exits with:

| Code | Means |
|---|---|
| 0 | Ran, and found nothing wrong |
| 1 | Ran, and found problems (validation failures, rows rejected, findings) |
| 2 | Could not run: bad usage, missing input, or an unhandled error |

This is the contract `grep` and `ruff` follow, and it lets a pipeline tell "the data is bad" apart
from "the job is broken". `argparse` already exits 2 on a usage error. Wrap `main()` so an
unhandled exception also exits 2 instead of Python's default 1:

```python
def main(argv: list[str] | None = None) -> int:
    ...

if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:  # noqa: BLE001 - the exit-code contract needs every failure
        print(f"error: {exc}", file=sys.stderr)
        sys.exit(2)
```

## The CI pipeline

The standard does not fix a CI system. It fixes the steps every CI run performs:

1. Select the Python version from `requires-python`.
2. Install uv (`python -m pip install uv`), or use the pip fallback.
3. Run `uv sync --locked`, which fails if `uv.lock` is out of date with `pyproject.toml`.
4. Run `uv run ruff check .` and `uv run ruff format --check .`.
5. Run `uv run pytest`, with JUnit output where the CI system shows test results.
6. Run `python-project check` where the auditor is available, so a drift from the standard fails
   the build. It ships with this skill set, not with the project, so a CI agent has it only if the
   repository vendors `python_project.py` or the pipeline fetches it. The generated template does
   neither; without it, run `check` locally before the PR.

`python-project` ships one implementation: an Azure DevOps template,
`python-project/scripts/templates/azure-pipelines.python.yml`. It uses `UsePythonVersion@0` for
step 1, a `useUv: false` parameter to switch to the pip fallback, and `PublishTestResults@2` for
the JUnit file.

On GitHub Actions, run the same steps in a workflow: `actions/setup-python` (or
`astral-sh/setup-uv`) with the version from `requires-python`, then `uv sync --locked`,
`uv run ruff check .`, `uv run ruff format --check .` and `uv run pytest` as `run:` steps, plus
`python-project check` if the auditor is available (step 6). `new` does not generate a GitHub workflow.

## Applying the standard

| Situation | Command |
|---|---|
| New, empty project | `/python-project new --kind script` (or `--kind package`); a dry run first, then `--apply` |
| Existing project | `/python-project check`; fix the findings, then run it again until it exits 0 |
| Code review of a Python change | Run `check` on the project the change touches |

## Related skills

| Need | Skill |
|---|---|
| pyright and pre-commit hooks on top of this standard | `python-quality-install` |
| Code-level rules those tools enforce (typing, exception chaining, no `Any`) | `python-quality-developer` |

`python-quality-install` writes its own ruff rule set and line length. In a project that follows
this standard, merge its `[tool.ruff]` settings into the existing table rather than adding a
second one, put its dev tools in `[dependency-groups] dev`, and keep `target-version` matching
`requires-python`.

## Not yet decided

- Type checking. The standard does not require a type checker. A project that wants one runs
  `python-quality-install`, which adds pyright in `basic` mode and a pre-commit hook. mypy also
  works, and Astral's `ty` would fit beside uv and ruff once it is stable.
