---
name: python-project
description: Scaffold a Python project, or audit an existing one against the python-architect standard
arguments:
  - name: mode
    description: "`check` to audit an existing project, or `new` to scaffold one"
    required: true
  - name: path
    description: Project root (default the current directory)
    required: false
---

# Python Project

Scaffold or audit a Python project against the python-architect standard: pyproject.toml, PEP 735 dependency
groups, uv with uv.lock, ruff, pytest pythonpath, requires-python and an Azure DevOps pipeline (GitHub Actions users run the same steps).

**REQUIRED:** Use the `python-project` skill to execute the workflow. It holds the script path,
the rules and the flags.

## Parameters

- **mode:** {{ mode }}
- **path:** {{ path | default: "." }}

## Execute

- **check:** run `python_project.py check --path <path>`, show the findings, and offer to fix
  each one. Run `check` again until it exits 0.
- **new:** ask for the project name and the kind (`script` or `package`), run the dry run, show
  it, and run with `--apply` only after the user confirms. Then run the verification commands the
  skill lists.
