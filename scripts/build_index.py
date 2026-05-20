#!/usr/bin/env python3
"""Scan skills/, validate against the Agent Skills spec, emit index.json,
and update the skills table in README.md plus the directory tree in
documentation/ARCHITECTURE.md.

Exit code: 0 on success, 1 if any skill fails validation.
"""
from __future__ import annotations

import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

import yaml

REPO = Path(__file__).resolve().parent.parent
SKILLS_DIR = REPO / "skills"
INDEX_PATH = REPO / "index.json"
README_PATH = REPO / "README.md"
ARCH_PATH = REPO / "documentation" / "ARCHITECTURE.md"

# Agent Skills spec (https://agentskills.io/specification)
NAME_RE = re.compile(r"^[a-z0-9][a-z0-9-]*[a-z0-9]$")
NON_STANDARD = {"autoInvoke", "priority", "triggers", "tools"}
KNOWN_OPTIONAL = {"license", "compatibility", "metadata", "allowed-tools"}
CLAUDE_EXTENSIONS = {
    "disable-model-invocation", "user-invocable", "model", "context",
    "agent", "hooks", "argument-hint",
}
KNOWN_FIELDS = {"name", "description"} | KNOWN_OPTIONAL | CLAUDE_EXTENSIONS


def parse_skill(skill_dir: Path):
    """Return (record-or-None, errors, warnings)."""
    errors: list[str] = []
    warnings: list[str] = []
    md = skill_dir / "SKILL.md"
    label = skill_dir.name

    if not md.is_file():
        errors.append(f"{label}: missing SKILL.md")
        return None, errors, warnings

    text = md.read_text(encoding="utf-8")
    m = re.match(r"^---\s*\n(.*?)\n---\s*\n", text, re.DOTALL)
    if not m:
        errors.append(f"{label}: missing YAML frontmatter (--- delimiters)")
        return None, errors, warnings

    try:
        fm = yaml.safe_load(m.group(1)) or {}
    except yaml.YAMLError as e:
        errors.append(f"{label}: invalid YAML frontmatter ({e})")
        return None, errors, warnings

    if not isinstance(fm, dict):
        errors.append(f"{label}: frontmatter is not a mapping")
        return None, errors, warnings

    name = (fm.get("name") or "").strip()
    desc = (fm.get("description") or "").strip()
    metadata = fm.get("metadata") or {}
    category = ""
    order: int | None = None
    if isinstance(metadata, dict):
        category = (metadata.get("category") or "").strip()
        raw_order = metadata.get("order")
        if raw_order is not None:
            try:
                order = int(raw_order)
            except (TypeError, ValueError):
                errors.append(f"{label}: metadata.order must be an integer, got {raw_order!r}")

    if not name:
        errors.append(f"{label}: missing required field 'name'")
    else:
        if name != skill_dir.name:
            errors.append(f"{label}: name '{name}' does not match directory name")
        if len(name) > 64:
            errors.append(f"{label}: name exceeds 64 characters ({len(name)})")
        if not NAME_RE.match(name) and len(name) > 1:
            errors.append(
                f"{label}: name '{name}' does not match required format "
                "(lowercase letters, digits, hyphens; no leading/trailing/consecutive hyphens)"
            )

    if not desc:
        errors.append(f"{label}: missing required field 'description'")
    elif len(desc) > 1024:
        warnings.append(f"{label}: description exceeds 1024 characters ({len(desc)})")

    for field in fm.keys():
        if field in NON_STANDARD:
            errors.append(f"{label}: non-standard field '{field}' (not in Agent Skills spec)")
        elif field not in KNOWN_FIELDS:
            warnings.append(f"{label}: unknown field '{field}'")

    allowed = fm.get("allowed-tools")
    if isinstance(allowed, str) and "," in allowed:
        errors.append(f"{label}: allowed-tools uses commas; spec requires space-delimited")

    if not category:
        errors.append(f"{label}: missing required field 'metadata.category'")
    elif not re.match(r"^[a-z0-9][a-z0-9-]*[a-z0-9]$", category):
        errors.append(f"{label}: metadata.category '{category}' must be lowercase-hyphenated")

    body = text[m.end():].strip()
    if not body:
        warnings.append(f"{label}: no body content after frontmatter")

    line_count = text.count("\n") + 1
    if line_count > 500:
        warnings.append(f"{label}: file exceeds 500 lines ({line_count}); consider splitting into references/")

    if errors:
        return None, errors, warnings

    short = desc.split(". ", 1)[0]
    if len(short) > 100:
        short = short[:97] + "..."

    return (
        {
            "name": name,
            "folder": skill_dir.name,
            "description": desc,
            "short_description": short,
            "category": category,
            "order": order,
        },
        errors,
        warnings,
    )


def scan():
    skills: list[dict] = []
    all_errors: list[str] = []
    all_warnings: list[str] = []
    for entry in sorted(SKILLS_DIR.iterdir(), key=lambda p: p.name):
        if not entry.is_dir():
            continue
        record, errors, warnings = parse_skill(entry)
        all_errors.extend(errors)
        all_warnings.extend(warnings)
        if record is not None:
            skills.append(record)
    return skills, all_errors, all_warnings


def _sort_key(s: dict) -> tuple[int, int, str]:
    # explicit order first (ascending), then alphabetical by name
    has_order = 0 if s["order"] is not None else 1
    order_val = s["order"] if s["order"] is not None else 0
    return (has_order, order_val, s["name"])


def write_index(skills: list[dict]) -> None:
    payload = {
        "generated": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "skills": [
            {
                "name": s["name"],
                "folder": s["folder"],
                "description": s["description"],
                "category": s["category"],
                "order": s["order"],
            }
            for s in skills
        ],
    }
    INDEX_PATH.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def update_readme(skills: list[dict]) -> list[str]:
    """Fill per-category SKILLS markers in README. Returns list of errors."""
    text = README_PATH.read_text(encoding="utf-8")

    # Group skills by category
    by_category: dict[str, list[dict]] = {}
    for s in skills:
        by_category.setdefault(s["category"], []).append(s)
    for cat in by_category:
        by_category[cat].sort(key=_sort_key)

    # Find every <!-- SKILLS: <slug> --> / <!-- END SKILLS: <slug> --> pair
    marker_re = re.compile(
        r"<!-- SKILLS: ([a-z0-9][a-z0-9-]*[a-z0-9]) -->.*?<!-- END SKILLS: \1 -->",
        re.DOTALL,
    )
    found_slugs: set[str] = set()

    def fill(match: re.Match[str]) -> str:
        slug = match.group(1)
        found_slugs.add(slug)
        bucket = by_category.get(slug, [])
        if not bucket:
            body = "_(no skills in this category yet)_"
        else:
            rows = ["| Skill | Description |", "|-------|-------------|"]
            for s in bucket:
                rows.append(
                    f"| [{s['name']}](skills/{s['folder']}/) | {s['short_description']} |"
                )
            body = "\n".join(rows)
        return f"<!-- SKILLS: {slug} -->\n{body}\n<!-- END SKILLS: {slug} -->"

    new = marker_re.sub(fill, text)
    README_PATH.write_text(new, encoding="utf-8")

    # Orphan check: every category referenced by a skill must have a README block
    orphan_errors: list[str] = []
    for cat, members in sorted(by_category.items()):
        if cat not in found_slugs:
            names = ", ".join(m["name"] for m in members)
            orphan_errors.append(
                f"category '{cat}' has skills ({names}) but no '<!-- SKILLS: {cat} -->' block in README.md"
            )
    return orphan_errors


def render_tree(skills: list[dict]) -> str:
    L, T, V, H = "└", "├", "│", "─"
    corner = f"{L}{H}{H} "
    tee = f"{T}{H}{H} "
    vert = f"{V}   "
    blank = "    "

    lines = [
        "ai-plugins-and-skills/",
        f"{tee}LICENSE",
        f"{tee}README.md                     # Auto-generated skills table",
        f"{tee}index.json                    # Auto-generated skill index",
        f"{tee}documentation/",
        f"{vert}{tee}ARCHITECTURE.md           # Auto-generated directory tree",
        f"{vert}{tee}HOW-TO-DEV.md",
        f"{vert}{tee}README-CLAUDECODE.md",
        f"{vert}{tee}README-COPILOT-CLI.md",
        f"{vert}{corner}README-VSCODE-COPILOT.md",
        f"{tee}scripts/",
        f"{vert}{tee}build_index.py            # Manual + pre-commit entry point",
        f"{vert}{corner}git-hooks/",
        f"{vert}    {corner}pre-commit            # Runs build_index.py",
        f"{corner}skills/",
    ]
    for i, s in enumerate(skills):
        last = i == len(skills) - 1
        prefix = corner if last else tee
        child = blank if last else vert
        lines.append(f"    {prefix}{s['folder']}/")
        lines.append(f"    {child}{corner}SKILL.md")
    return "```\n" + "\n".join(lines) + "\n```"


def update_architecture(skills: list[dict]) -> None:
    if not ARCH_PATH.is_file():
        return
    text = ARCH_PATH.read_text(encoding="utf-8")
    tree = render_tree(skills)
    pattern = re.compile(r"(## Directory Structure\s*\n\n)```.*?```", re.DOTALL)
    if not pattern.search(text):
        print(f"warn: could not locate '## Directory Structure' code block in {ARCH_PATH}", file=sys.stderr)
        return
    ARCH_PATH.write_text(pattern.sub(lambda m: m.group(1) + tree, text), encoding="utf-8")


def main() -> int:
    skills, errors, warnings = scan()

    for w in warnings:
        print(f"  [WARN] {w}")
    for e in errors:
        print(f"  [ERROR] {e}", file=sys.stderr)

    if errors:
        print(f"\n{len(errors)} error(s), {len(warnings)} warning(s) - aborting.", file=sys.stderr)
        return 1

    skills.sort(key=lambda s: (s["category"],) + _sort_key(s))
    write_index(skills)
    orphans = update_readme(skills)
    if orphans:
        for o in orphans:
            print(f"  [ERROR] {o}", file=sys.stderr)
        print(f"\n{len(orphans)} orphan category error(s) - aborting.", file=sys.stderr)
        return 1
    update_architecture(skills)

    print(f"OK: {len(skills)} skills indexed across {len({s['category'] for s in skills})} categories, {len(warnings)} warning(s).")
    print(f"  - {INDEX_PATH.relative_to(REPO)}")
    print(f"  - {README_PATH.relative_to(REPO)}")
    print(f"  - {ARCH_PATH.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
