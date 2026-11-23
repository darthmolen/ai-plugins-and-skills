# Dependency audit: Python

**Markers:** `pyproject.toml`, `uv.lock`, `requirements*.txt` or `pylock.toml`.

The audit covers the packages the code runs with, not the development groups. A test or lint tool
does not ship.

## A uv project (`uv.lock`)

```bash
uv lock --check                       # the lockfile matches pyproject.toml; exit 1 if it is stale
uv export --frozen --no-default-groups --no-emit-project --quiet -o "$SCRATCH/requirements.txt"
uvx pip-audit -r "$SCRATCH/requirements.txt" --disable-pip
```

- `uv lock --check` comes first: a stale lockfile means the audit reads versions that are not the
  ones that will be installed. A stale lock is a finding in itself; run `uv lock` and commit it.
- `--no-default-groups` leaves out every default dependency group (`dev`, and any others the project
  lists in `default-groups`), so only the runtime dependencies and extras remain.
- `--disable-pip` audits the exported pins as they are, without resolving anything. It needs the
  hashes `uv export` writes by default, so do not add `--no-hashes`.
- `pip-audit` exits 1 when it finds a vulnerability and 0 when it finds none.

`uv audit --locked --no-default-groups` does the same in one step, but uv marks it experimental, so
use it only where the pipeline already does.

`pip-audit --locked` reads `pylock.toml` files, not `uv.lock`; it reports "no lockfiles found" on a
uv project.

## A `requirements.txt` project

```bash
uvx pip-audit -r requirements.txt
```

Where every line is pinned with `==`, add `--no-deps --disable-pip` to audit the pins as written
instead of resolving them. Audit the runtime requirements file, not `requirements-dev.txt`.

## A project with only `pyproject.toml`

There is nothing pinned to audit, so the result would depend on what resolves today. Say so in the
report rather than auditing a resolution nobody installs. The `python-project` skill audits a
project against the standard that adds a lockfile.

## Do not

- Pass `--ignore-vuln` (or `uv audit --ignore`) to make the PR pass. An ignored advisory is a
  decision for the repository's owners, with its reason recorded; propose it in the report instead.
- Run `pip-audit --fix`. It upgrades packages in the environment, which is not where the pins live.

## A finding

Raise the pin to the fixed version the report names, in `pyproject.toml` with `uv lock`, or in the
requirements file. For a transitive package, `uv lock --upgrade-package <name>` raises it within the
existing constraints. Then run the audit again.
