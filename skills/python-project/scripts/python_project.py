"""Scaffold or audit a Python project against the python-architect standard.

Exit codes: 0 success, 1 problems found, 2 could not run.
"""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
import tomllib
from dataclasses import dataclass
from pathlib import Path

SYS_PATH_EDIT = re.compile(r"\bsys\.path\.(insert|append)\s*\(")
PROJECT_NAME = re.compile(r"^[A-Za-z][A-Za-z0-9._-]*$")
PYTHON_VERSION = re.compile(r"^3\.(\d+)$")
PLACEHOLDER = re.compile(r"@[a-z_]+@")
TEMPLATES = Path(__file__).with_name("templates")

# For each kind: published path -> template file. "@module@" in a path is substituted too.
LAYOUTS = {
    "script": {
        "pyproject.toml": "pyproject.script.toml",
        ".gitignore": "gitignore",
        "azure-pipelines.yml": "azure-pipelines.python.yml",
        "tools/@module@.py": "tool.py.tmpl",
        "tests/test_@module@.py": "test_tool.py.tmpl",
    },
    "package": {
        "pyproject.toml": "pyproject.package.toml",
        ".gitignore": "gitignore",
        "azure-pipelines.yml": "azure-pipelines.python.yml",
        "src/@module@/__init__.py": "package_init.py.tmpl",
        "tests/test_@module@.py": "test_package.py.tmpl",
    },
}


class CannotRun(Exception):
    """The audit or scaffold could not run: bad input, not bad project."""


@dataclass(frozen=True)
class Finding:
    rule: str
    where: str
    message: str

    def __str__(self) -> str:
        return f"{self.rule} {self.where} {self.message}"


def check_project(root: Path) -> list[Finding]:
    if not root.is_dir():
        raise CannotRun(f"{root} is not a directory")

    findings: list[Finding] = []

    for req in sorted(root.glob("requirements*.txt")):
        findings.append(Finding("REQUIREMENTS-TXT", req.name, "move these into pyproject.toml"))

    for name in ("setup.py", "setup.cfg"):
        if (root / name).is_file():
            findings.append(
                Finding("SETUP-FILE", name, "project metadata belongs in pyproject.toml")
            )

    pyproject = root / "pyproject.toml"
    if not pyproject.is_file():
        findings.append(Finding("NO-PYPROJECT", "pyproject.toml", "missing"))
    else:
        findings.extend(check_pyproject(pyproject))

    if not (root / "uv.lock").is_file():
        findings.append(Finding("NO-UV-LOCK", "uv.lock", "missing; run `uv lock` and commit it"))

    tests = root / "tests"
    if tests.is_dir():
        for py in sorted(tests.rglob("*.py")):
            findings.extend(check_sys_path(root, py))

    return findings


def check_pyproject(pyproject: Path) -> list[Finding]:
    try:
        data = tomllib.loads(pyproject.read_text(encoding="utf-8"))
    except (tomllib.TOMLDecodeError, UnicodeDecodeError) as exc:
        raise CannotRun(f"pyproject.toml could not be parsed: {exc}") from exc

    findings: list[Finding] = []
    if "requires-python" not in data.get("project", {}):
        findings.append(
            Finding("NO-REQUIRES-PYTHON", "pyproject.toml", "set [project] requires-python")
        )
    if "dependency-groups" not in data:
        findings.append(
            Finding(
                "NO-DEPENDENCY-GROUPS",
                "pyproject.toml",
                "put dev tools in a PEP 735 [dependency-groups] table",
            )
        )
    if "ruff" not in data.get("tool", {}):
        findings.append(Finding("NO-RUFF-CONFIG", "pyproject.toml", "add a [tool.ruff] table"))
    return findings


def check_sys_path(root: Path, py: Path) -> list[Finding]:
    rel = py.relative_to(root).as_posix()
    findings = []
    for number, line in enumerate(py.read_text(encoding="utf-8").splitlines(), start=1):
        if SYS_PATH_EDIT.search(line):
            findings.append(
                Finding(
                    "SYS-PATH-EDIT",
                    f"{rel}:{number}",
                    "use [tool.pytest.ini_options] pythonpath instead",
                )
            )
    return findings


def cmd_check(args: argparse.Namespace) -> int:
    findings = check_project(Path(args.path))
    for finding in findings:
        print(finding)
    print(f"{len(findings)} finding(s)")
    return 1 if findings else 0


def render(text: str, values: dict[str, str]) -> str:
    for key, value in values.items():
        text = text.replace(f"@{key}@", value)
    leftover = PLACEHOLDER.search(text)
    if leftover:
        raise CannotRun(f"template placeholder {leftover.group()} has no value")
    return text


def plan_project(kind: str, name: str, python: str) -> dict[str, str]:
    """Return published path -> rendered content for a new project."""
    if not PROJECT_NAME.match(name):
        raise CannotRun(f"project name {name!r} must start with a letter: letters, digits, . _ -")
    version = PYTHON_VERSION.match(python)
    if not version:
        raise CannotRun(f"--python {python!r} must look like 3.11")
    values = {
        "name": name,
        "module": re.sub(r"[.-]", "_", name).lower(),
        "python_version": python,
        "target_version": f"py3{version.group(1)}",
    }
    return {
        render(path, values): render((TEMPLATES / template).read_text(encoding="utf-8"), values)
        for path, template in LAYOUTS[kind].items()
    }


def cmd_new(args: argparse.Namespace) -> int:
    root = Path(args.path)
    files = plan_project(args.kind, args.name, args.python)
    collisions = [rel for rel in files if (root / rel).exists()]

    for rel in files:
        print(f"{'COLLISION' if rel in collisions else 'WOULD WRITE'} {rel}")
    if collisions:
        print(f"{len(collisions)} collision(s); nothing written")
        return 1
    if not args.apply:
        print("dry run; pass --apply to write")
        return 0

    for rel, content in files.items():
        target = root / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8", newline="\n")
    print(f"wrote {len(files)} file(s)")

    if args.no_lock:
        return 0
    if shutil.which("uv") is None:
        print("uv not found; install it and run `uv lock`, then commit uv.lock")
        return 0
    result = subprocess.run(["uv", "lock"], cwd=root)
    if result.returncode != 0:
        raise CannotRun("`uv lock` failed; the files are written, run it again once fixed")
    print("locked: uv.lock written; commit it")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="python_project", description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    check = sub.add_parser("check", help="audit a project against the standard")
    check.add_argument("--path", default=".", help="project root (default: current directory)")
    check.set_defaults(func=cmd_check)

    new = sub.add_parser("new", help="scaffold a project; a dry run unless --apply")
    new.add_argument("--kind", required=True, choices=sorted(LAYOUTS))
    new.add_argument("--name", required=True, help="project name, e.g. billing-export")
    new.add_argument("--path", default=".", help="project root (default: current directory)")
    new.add_argument("--python", default="3.11", help="lowest runtime (default: 3.11, Fabric 1.3)")
    new.add_argument("--apply", action="store_true", help="write the files")
    new.add_argument("--no-lock", action="store_true", help="skip `uv lock` after writing")
    new.set_defaults(func=cmd_new)

    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        return args.func(args)
    except CannotRun as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:  # noqa: BLE001 - the exit-code contract needs every failure
        print(f"error: {exc}", file=sys.stderr)
        sys.exit(2)
