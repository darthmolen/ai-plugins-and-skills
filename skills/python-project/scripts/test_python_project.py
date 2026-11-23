"""pytest suite for python_project.py. Run with: python -m pytest scripts -q"""

from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path

import pytest

import python_project

SCRIPT = Path(__file__).with_name("python_project.py")

CLEAN_PYPROJECT = """\
[project]
name = "demo"
version = "0.1.0"
requires-python = ">=3.11"
dependencies = []

[dependency-groups]
dev = ["pytest>=8", "ruff>=0.7"]

[tool.ruff]
target-version = "py311"

[tool.pytest.ini_options]
pythonpath = ["tools"]
testpaths = ["tests"]
"""


def make_clean_project(root: Path) -> Path:
    """A project that meets every rule, for each test to break one at a time."""
    (root / "pyproject.toml").write_text(CLEAN_PYPROJECT, encoding="utf-8")
    (root / "uv.lock").write_text("version = 1\n", encoding="utf-8")
    (root / "tools").mkdir()
    (root / "tools" / "demo.py").write_text("def answer():\n    return 42\n", encoding="utf-8")
    (root / "tests").mkdir()
    (root / "tests" / "test_demo.py").write_text(
        "from demo import answer\n\n\ndef test_answer():\n    assert answer() == 42\n",
        encoding="utf-8",
    )
    return root


def run_check(root: Path, capsys) -> tuple[int, str]:
    code = python_project.main(["check", "--path", str(root)])
    out = capsys.readouterr()
    return code, out.out + out.err


# ── check: the clean baseline ────────────────────────────────────────────


def test_check_clean_project_exits_0_and_reports_no_findings(tmp_path, capsys):
    code, out = run_check(make_clean_project(tmp_path), capsys)
    assert code == 0
    assert "0 finding(s)" in out


# ── check: one rule broken at a time ─────────────────────────────────────


def test_check_flags_bare_requirements_txt(tmp_path, capsys):
    root = make_clean_project(tmp_path)
    (root / "requirements-dev.txt").write_text("pytest\n", encoding="utf-8")
    code, out = run_check(root, capsys)
    assert code == 1
    assert "REQUIREMENTS-TXT requirements-dev.txt" in out


def test_check_flags_setup_py(tmp_path, capsys):
    root = make_clean_project(tmp_path)
    (root / "setup.py").write_text("from setuptools import setup\nsetup()\n", encoding="utf-8")
    code, out = run_check(root, capsys)
    assert code == 1
    assert "SETUP-FILE setup.py" in out


def test_check_flags_setup_cfg(tmp_path, capsys):
    root = make_clean_project(tmp_path)
    (root / "setup.cfg").write_text("[metadata]\nname = demo\n", encoding="utf-8")
    code, out = run_check(root, capsys)
    assert code == 1
    assert "SETUP-FILE setup.cfg" in out


def test_check_flags_missing_pyproject(tmp_path, capsys):
    root = make_clean_project(tmp_path)
    (root / "pyproject.toml").unlink()
    code, out = run_check(root, capsys)
    assert code == 1
    assert "NO-PYPROJECT" in out


def test_check_flags_missing_uv_lock(tmp_path, capsys):
    root = make_clean_project(tmp_path)
    (root / "uv.lock").unlink()
    code, out = run_check(root, capsys)
    assert code == 1
    assert "NO-UV-LOCK" in out


@pytest.mark.parametrize(
    ("removed", "rule"),
    [
        ('requires-python = ">=3.11"\n', "NO-REQUIRES-PYTHON"),
        ('[dependency-groups]\ndev = ["pytest>=8", "ruff>=0.7"]\n', "NO-DEPENDENCY-GROUPS"),
        ('[tool.ruff]\ntarget-version = "py311"\n', "NO-RUFF-CONFIG"),
    ],
    ids=["requires-python", "dependency-groups", "ruff"],
)
def test_check_flags_missing_pyproject_table(tmp_path, capsys, removed, rule):
    root = make_clean_project(tmp_path)
    pyproject = root / "pyproject.toml"
    text = pyproject.read_text(encoding="utf-8")
    assert removed in text
    pyproject.write_text(text.replace(removed, ""), encoding="utf-8")
    code, out = run_check(root, capsys)
    assert code == 1
    assert rule in out
    # Only the one rule broken: the others still pass.
    assert out.count("NO-") == 1


@pytest.mark.parametrize("call", ["insert(0, '..')", "append('../tools')"])
def test_check_flags_sys_path_edit_in_tests_with_line(tmp_path, capsys, call):
    root = make_clean_project(tmp_path)
    (root / "tests" / "test_path.py").write_text(f"import sys\nsys.path.{call}\n", encoding="utf-8")
    code, out = run_check(root, capsys)
    assert code == 1
    assert "SYS-PATH-EDIT tests/test_path.py:2" in out


def test_check_ignores_sys_path_edit_outside_tests(tmp_path, capsys):
    root = make_clean_project(tmp_path)
    (root / "tools" / "launcher.py").write_text(
        "import sys\nsys.path.insert(0, '.')\n", encoding="utf-8"
    )
    code, _ = run_check(root, capsys)
    assert code == 0


def test_check_ignores_notebooks(tmp_path, capsys):
    root = make_clean_project(tmp_path)
    (root / "tests" / "explore.ipynb").write_text(
        '{"cells": [{"source": ["import sys\\n", "sys.path.insert(0, \'..\')"]}]}',
        encoding="utf-8",
    )
    code, _ = run_check(root, capsys)
    assert code == 0


def test_check_reports_every_finding_not_just_the_first(tmp_path, capsys):
    root = make_clean_project(tmp_path)
    (root / "requirements-dev.txt").write_text("pytest\n", encoding="utf-8")
    (root / "tests" / "test_path.py").write_text(
        "import sys\nsys.path.insert(0, '..')\n", encoding="utf-8"
    )
    code, out = run_check(root, capsys)
    assert code == 1
    assert "REQUIREMENTS-TXT requirements-dev.txt" in out
    assert "SYS-PATH-EDIT tests/test_path.py:2" in out
    assert "2 finding(s)" in out


# ── check: could not run ─────────────────────────────────────────────────


def test_check_missing_directory_exits_2(tmp_path, capsys):
    code, out = run_check(tmp_path / "nope", capsys)
    assert code == 2
    assert "not a directory" in out


def test_check_unparseable_pyproject_exits_2(tmp_path, capsys):
    root = make_clean_project(tmp_path)
    (root / "pyproject.toml").write_text("[project\nname = ", encoding="utf-8")
    code, out = run_check(root, capsys)
    assert code == 2
    assert "pyproject.toml" in out


def test_cli_usage_error_exits_2():
    result = subprocess.run(
        [sys.executable, "-I", str(SCRIPT), "frobnicate"],
        capture_output=True,
        text=True,
    )
    assert result.returncode == 2


def run_new(root: Path, capsys, *extra: str) -> tuple[int, str]:
    code = python_project.main(["new", "--path", str(root), *extra])
    out = capsys.readouterr()
    return code, out.out + out.err


def written_files(root: Path) -> set[str]:
    return {p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file()}


SCRIPT_FILES = {
    "pyproject.toml",
    ".gitignore",
    "azure-pipelines.yml",
    "tools/billing_export.py",
    "tests/test_billing_export.py",
}

PACKAGE_FILES = {
    "pyproject.toml",
    ".gitignore",
    "azure-pipelines.yml",
    "src/billing_export/__init__.py",
    "tests/test_billing_export.py",
}


# ── new: dry run, apply, collisions ──────────────────────────────────────


def test_new_dry_run_lists_files_and_writes_nothing(tmp_path, capsys):
    code, out = run_new(tmp_path, capsys, "--kind", "script", "--name", "billing-export")
    assert code == 0
    assert written_files(tmp_path) == set()
    for rel in SCRIPT_FILES:
        assert f"WOULD WRITE {rel}" in out


@pytest.mark.parametrize(
    ("kind", "expected"),
    [("script", SCRIPT_FILES), ("package", PACKAGE_FILES)],
    ids=["script", "package"],
)
def test_new_apply_writes_the_kind_and_passes_check_except_the_lock(
    tmp_path, capsys, kind, expected
):
    code, _ = run_new(
        tmp_path, capsys, "--kind", kind, "--name", "billing-export", "--apply", "--no-lock"
    )
    assert code == 0
    assert written_files(tmp_path) == expected
    findings = python_project.check_project(tmp_path)
    assert [f.rule for f in findings] == ["NO-UV-LOCK"]


def test_new_collision_writes_nothing_and_exits_1(tmp_path, capsys):
    (tmp_path / "pyproject.toml").write_text("# mine\n", encoding="utf-8")
    code, out = run_new(
        tmp_path, capsys, "--kind", "script", "--name", "billing-export", "--apply", "--no-lock"
    )
    assert code == 1
    assert "COLLISION pyproject.toml" in out
    assert written_files(tmp_path) == {"pyproject.toml"}
    assert (tmp_path / "pyproject.toml").read_text(encoding="utf-8") == "# mine\n"


def test_new_substitutes_every_placeholder(tmp_path, capsys):
    run_new(
        tmp_path, capsys, "--kind", "package", "--name", "billing-export", "--apply", "--no-lock"
    )
    for rel in PACKAGE_FILES:
        text = (tmp_path / rel).read_text(encoding="utf-8")
        assert not re.search(r"@[a-z_]+@", text), rel


def test_render_refuses_a_placeholder_with_no_value():
    with pytest.raises(python_project.CannotRun, match="@nope@"):
        python_project.render("name = @name@ @nope@", {"name": "x"})


def test_new_python_version_sets_requires_python_ruff_and_pipeline(tmp_path, capsys):
    run_new(
        tmp_path,
        capsys,
        "--kind",
        "script",
        "--name",
        "billing-export",
        "--python",
        "3.12",
        "--apply",
        "--no-lock",
    )
    pyproject = (tmp_path / "pyproject.toml").read_text(encoding="utf-8")
    assert 'requires-python = ">=3.12"' in pyproject
    assert 'target-version = "py312"' in pyproject
    assert "'3.12'" in (tmp_path / "azure-pipelines.yml").read_text(encoding="utf-8")


def test_new_default_python_is_the_fabric_runtime(tmp_path, capsys):
    run_new(
        tmp_path, capsys, "--kind", "script", "--name", "billing-export", "--apply", "--no-lock"
    )
    assert 'requires-python = ">=3.11"' in (tmp_path / "pyproject.toml").read_text(encoding="utf-8")


@pytest.mark.parametrize("bad", ["1billing", "billing export", ""])
def test_new_rejects_bad_name_with_exit_2(tmp_path, capsys, bad):
    code, _ = run_new(tmp_path, capsys, "--kind", "script", "--name", bad)
    assert code == 2


@pytest.mark.parametrize("kind", ["script", "package"])
@pytest.mark.parametrize("keyword", ["class", "Class", "import", "lambda"])
def test_new_rejects_name_whose_module_is_a_keyword_and_writes_nothing(
    tmp_path, capsys, kind, keyword
):
    # `from class import main` cannot be imported, so the scaffold would fail its own tests.
    code, _ = run_new(tmp_path, capsys, "--kind", kind, "--name", keyword, "--apply", "--no-lock")
    assert code == 2
    assert list(tmp_path.iterdir()) == []


def test_new_accepts_name_that_only_contains_a_keyword(tmp_path, capsys):
    code, _ = run_new(tmp_path, capsys, "--kind", "script", "--name", "import-lib")
    assert code == 0


def test_new_rejects_bad_python_version_with_exit_2(tmp_path, capsys):
    code, _ = run_new(tmp_path, capsys, "--kind", "script", "--name", "x", "--python", "3")
    assert code == 2


def test_new_without_uv_writes_files_and_says_to_lock(tmp_path, capsys, monkeypatch):
    monkeypatch.setattr(python_project.shutil, "which", lambda _: None)
    code, out = run_new(tmp_path, capsys, "--kind", "script", "--name", "billing-export", "--apply")
    assert code == 0
    assert "pyproject.toml" in written_files(tmp_path)
    assert "uv not found" in out


@pytest.mark.parametrize("kind", ["script", "package"])
def test_new_project_own_tests_pass(tmp_path, capsys, kind):
    run_new(tmp_path, capsys, "--kind", kind, "--name", "billing-export", "--apply", "--no-lock")
    env = {k: v for k, v in os.environ.items() if k != "PYTHONPATH"}
    if kind == "package":
        env["PYTHONPATH"] = str(tmp_path / "src")  # stands in for the editable install
    result = subprocess.run(
        [sys.executable, "-m", "pytest", "-q", "-p", "no:cacheprovider"],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        env=env,
    )
    assert result.returncode == 0, result.stdout + result.stderr


def test_cli_check_exit_code_reaches_the_shell(tmp_path):
    root = make_clean_project(tmp_path)
    (root / "setup.py").write_text("", encoding="utf-8")
    result = subprocess.run(
        [sys.executable, "-I", str(SCRIPT), "check", "--path", str(root)],
        capture_output=True,
        text=True,
    )
    assert result.returncode == 1
    assert "SETUP-FILE setup.py" in result.stdout
