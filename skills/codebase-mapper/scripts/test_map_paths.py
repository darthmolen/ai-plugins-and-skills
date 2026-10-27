#!/usr/bin/env python3
"""
The mappers must produce the same map on Windows as on Linux.

Paths are formatted with forward slashes, and directories sort in POSIX
order. On Windows, str(Path) gives backslashes, and "\\" sorts after
uppercase letters where "/" sorts before them, so a map built there differs
in both separators and order.

Run:
    python -m pytest skills/codebase-mapper/scripts -q
"""

import subprocess
import sys
from pathlib import Path

import pytest

SCRIPTS = Path(__file__).parent

# "a/z" sorts before "aB" with "/" (0x2F < 'B' 0x42); "a\z" sorts after it
# with "\" (0x5C > 0x42).
FIXTURES = {
    "map_python.py": {
        "a/z/deep.py": "class Deep:\n    def run(self):\n        pass\n",
        "aB/flat.py": "class Flat:\n    def run(self):\n        pass\n",
    },
    "map_typescript.py": {
        "a/z/deep.ts": "export class Deep {\n  run(): void {}\n}\n",
        "aB/flat.ts": "export class Flat {\n  run(): void {}\n}\n",
    },
}


def run_mapper(script: str, root: Path, out: Path) -> str:
    for rel, body in FIXTURES[script].items():
        path = root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(body, encoding="utf-8")

    subprocess.run(
        [sys.executable, str(SCRIPTS / script), "--root", str(root), "--output", str(out)],
        check=True, capture_output=True, text=True,
    )
    return out.read_text(encoding="utf-8")


@pytest.mark.parametrize("script", sorted(FIXTURES))
def test_nested_directories_use_forward_slashes(tmp_path, script):
    output = run_mapper(script, tmp_path / "src", tmp_path / "map.md")

    assert "- `a/z/`" in output
    assert "\\" not in output


@pytest.mark.parametrize("script", sorted(FIXTURES))
def test_directories_sort_in_posix_order(tmp_path, script):
    output = run_mapper(script, tmp_path / "src", tmp_path / "map.md")

    assert output.index("- `a/z/`") < output.index("- `aB/`")
