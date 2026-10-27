#!/usr/bin/env python3
"""
Tests for update_claude_md.py.

The map is written to its own file; CLAUDE.md only carries a pointer to it,
so the map is not loaded into every session.

Run:
    python -m pytest skills/codebase-mapper/scripts -q
"""

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).parent))

import update_claude_md as ucm  # noqa: E402


MAP_BODY = "### Python: src/\n- `api/users.py`: `UserRouter` → get_user(), create_user()\n"

HAND_WRITTEN_BEFORE = "# CLAUDE.md\n\n## Project Context\n\n- Hand-written context.\n"
HAND_WRITTEN_AFTER = "\n## Other Section\n\nKeep me exactly as I am.\n"


def make_repo(tmp_path: Path, dirs: tuple[str, ...] = ()) -> Path:
    for d in dirs:
        (tmp_path / d).mkdir()
    claude_md = tmp_path / "CLAUDE.md"
    claude_md.write_text(HAND_WRITTEN_BEFORE + HAND_WRITTEN_AFTER, encoding="utf-8")
    map_file = tmp_path / "map_python.md"
    map_file.write_text(MAP_BODY, encoding="utf-8")
    return claude_md


def run(claude_md: Path, *extra: str) -> None:
    ucm.main(["--claude-md", str(claude_md), "--maps", str(claude_md.parent / "map_python.md"), *extra])


def marker_block(text: str) -> str:
    start = text.index(ucm.START_MARKER)
    end = text.index(ucm.END_MARKER) + len(ucm.END_MARKER)
    return text[start:end]


# --- where the map file goes ---------------------------------------------

@pytest.mark.parametrize(
    "dirs, expected",
    [
        (("documentation",), "documentation"),
        (("docs",), "docs"),
        (("documentation", "docs"), "documentation"),
        ((), "documentation"),
    ],
)
def test_map_file_lands_in_existing_docs_folder(tmp_path, dirs, expected):
    claude_md = make_repo(tmp_path, dirs)

    run(claude_md)

    map_path = tmp_path / expected / "CODEBASE-MAP.md"
    assert map_path.exists()
    assert "UserRouter" in map_path.read_text(encoding="utf-8")
    other = {"documentation", "docs"} - {expected}
    for name in other:
        assert not (tmp_path / name / "CODEBASE-MAP.md").exists()


def test_map_file_override(tmp_path):
    claude_md = make_repo(tmp_path, ("documentation",))

    run(claude_md, "--map-file", str(tmp_path / "custom" / "MAP.md"))

    assert "UserRouter" in (tmp_path / "custom" / "MAP.md").read_text(encoding="utf-8")
    assert not (tmp_path / "documentation" / "CODEBASE-MAP.md").exists()
    assert "`custom/MAP.md`" in claude_md.read_text(encoding="utf-8")


# --- what goes into CLAUDE.md ---------------------------------------------

def test_claude_md_gets_pointer_not_map(tmp_path):
    claude_md = make_repo(tmp_path, ("documentation",))

    run(claude_md)

    text = claude_md.read_text(encoding="utf-8")
    block = marker_block(text)
    assert "`documentation/CODEBASE-MAP.md`" in block
    assert "UserRouter" not in text
    assert "get_user" not in text


def test_pointer_is_never_an_at_import(tmp_path):
    # A CLAUDE.md "@path" import is inlined at load time, which would bring
    # the whole map back into every session.
    claude_md = make_repo(tmp_path, ("documentation",))

    run(claude_md)

    block = marker_block(claude_md.read_text(encoding="utf-8"))
    assert "@" not in block.replace(ucm.START_MARKER, "").replace(ucm.END_MARKER, "")


def test_inlined_map_migrates_to_pointer_and_frame_survives(tmp_path):
    claude_md = make_repo(tmp_path, ("documentation",))
    inlined = (
        HAND_WRITTEN_BEFORE
        + f"\n{ucm.START_MARKER}\n## Architecture Map\n\n{MAP_BODY}{ucm.END_MARKER}\n"
        + HAND_WRITTEN_AFTER
    )
    claude_md.write_text(inlined, encoding="utf-8")
    before, after = inlined.split(ucm.START_MARKER)[0], inlined.split(ucm.END_MARKER)[1]

    run(claude_md)

    text = claude_md.read_text(encoding="utf-8")
    assert "UserRouter" not in text
    assert "`documentation/CODEBASE-MAP.md`" in marker_block(text)
    assert text.split(ucm.START_MARKER)[0] == before
    assert text.split(ucm.END_MARKER)[1] == after


def test_fresh_insert_preserves_hand_written_text(tmp_path):
    claude_md = make_repo(tmp_path, ("documentation",))

    run(claude_md)

    text = claude_md.read_text(encoding="utf-8")
    assert text.startswith(HAND_WRITTEN_BEFORE.rstrip())
    assert text.rstrip().endswith(HAND_WRITTEN_AFTER.strip())


def test_rerun_is_idempotent_for_claude_md(tmp_path):
    claude_md = make_repo(tmp_path, ("documentation",))

    run(claude_md)
    first = claude_md.read_bytes()
    run(claude_md)

    assert claude_md.read_bytes() == first


# --- remove -----------------------------------------------------------------

def test_remove_strips_block_and_deletes_map_file(tmp_path):
    claude_md = make_repo(tmp_path, ("documentation",))
    run(claude_md)

    ucm.main(["--claude-md", str(claude_md), "--remove"])

    text = claude_md.read_text(encoding="utf-8")
    assert ucm.START_MARKER not in text
    assert "Keep me exactly as I am." in text
    assert not (tmp_path / "documentation" / "CODEBASE-MAP.md").exists()
