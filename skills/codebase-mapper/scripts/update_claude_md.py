#!/usr/bin/env python3
"""
CLAUDE.md Updater

Merges codebase map outputs into CLAUDE.md using a marker-based system.
Preserves hand-written sections outside the markers.

Usage:
    python update_claude_md.py --claude-md ./CLAUDE.md --maps /tmp/map_python.md /tmp/map_csharp.md
    python update_claude_md.py --claude-md ./CLAUDE.md --maps /tmp/map_*.md --insert-after "## Project Context"
"""

import argparse
import sys
from pathlib import Path
from datetime import datetime


START_MARKER = "<!-- CODEBASE-MAP:AUTO-START -->"
END_MARKER = "<!-- CODEBASE-MAP:AUTO-END -->"

DEFAULT_INSERT_AFTER = "## Project Context"


def read_maps(map_files: list[Path]) -> str:
    """Read and combine map files."""
    sections = []

    for map_file in map_files:
        if not map_file.exists():
            print(f"Warning: Map file not found: {map_file}", file=sys.stderr)
            continue

        content = map_file.read_text(encoding='utf-8').strip()
        if content:
            sections.append(content)

    if not sections:
        return ""

    # Combine with Architecture Map header
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M")
    header = f"## Architecture Map\n\n*Auto-generated on {timestamp}*\n"

    return header + "\n\n" + "\n\n".join(sections)


def update_claude_md(claude_md_path: Path, map_content: str, insert_after: str) -> bool:
    """Update CLAUDE.md with map content using markers."""

    if not claude_md_path.exists():
        print(f"Error: CLAUDE.md not found: {claude_md_path}", file=sys.stderr)
        return False

    content = claude_md_path.read_text(encoding='utf-8')

    # Build the marked section
    marked_section = f"\n\n{START_MARKER}\n{map_content}\n{END_MARKER}\n"

    # Check if markers already exist
    start_idx = content.find(START_MARKER)
    end_idx = content.find(END_MARKER)

    if start_idx != -1 and end_idx != -1:
        # Replace content between markers
        new_content = (
            content[:start_idx] +
            START_MARKER + "\n" +
            map_content + "\n" +
            content[end_idx:]
        )
        print(f"Updated existing map section in {claude_md_path}")
    else:
        # Insert after specified section
        insert_pos = content.find(insert_after)
        if insert_pos == -1:
            # Insert at end if section not found
            new_content = content.rstrip() + marked_section
            print(f"Appended map section to end of {claude_md_path}")
        else:
            # Find end of the section (next ## or end of file)
            section_end = content.find("\n## ", insert_pos + len(insert_after))
            if section_end == -1:
                section_end = len(content)

            new_content = (
                content[:section_end].rstrip() +
                marked_section +
                content[section_end:]
            )
            print(f"Inserted map section after '{insert_after}' in {claude_md_path}")

    # Write updated content
    claude_md_path.write_text(new_content, encoding='utf-8')
    return True


def remove_map_section(claude_md_path: Path) -> bool:
    """Remove the map section from CLAUDE.md."""
    if not claude_md_path.exists():
        print(f"Error: CLAUDE.md not found: {claude_md_path}", file=sys.stderr)
        return False

    content = claude_md_path.read_text(encoding='utf-8')

    start_idx = content.find(START_MARKER)
    end_idx = content.find(END_MARKER)

    if start_idx == -1 or end_idx == -1:
        print("No map section found to remove")
        return True

    # Remove the section including surrounding whitespace
    before = content[:start_idx].rstrip()
    after = content[end_idx + len(END_MARKER):].lstrip()

    new_content = before + "\n\n" + after

    claude_md_path.write_text(new_content, encoding='utf-8')
    print(f"Removed map section from {claude_md_path}")
    return True


def count_tokens_estimate(text: str) -> int:
    """Rough estimate of token count (words * 1.3)."""
    words = len(text.split())
    return int(words * 1.3)


def main():
    parser = argparse.ArgumentParser(description='Update CLAUDE.md with codebase map')
    parser.add_argument('--claude-md', type=Path, required=True,
                        help='Path to CLAUDE.md')
    parser.add_argument('--maps', type=Path, nargs='*',
                        help='Map files to merge')
    parser.add_argument('--insert-after', type=str, default=DEFAULT_INSERT_AFTER,
                        help=f'Section header to insert after (default: "{DEFAULT_INSERT_AFTER}")')
    parser.add_argument('--remove', action='store_true',
                        help='Remove the map section instead of updating')
    parser.add_argument('--dry-run', action='store_true',
                        help='Show what would be done without making changes')
    args = parser.parse_args()

    if args.remove:
        if args.dry_run:
            print(f"Would remove map section from {args.claude_md}")
        else:
            remove_map_section(args.claude_md)
        return

    if not args.maps:
        print("Error: No map files specified", file=sys.stderr)
        sys.exit(1)

    # Handle glob patterns (shell expansion)
    map_files = []
    for pattern in args.maps:
        if '*' in str(pattern):
            map_files.extend(pattern.parent.glob(pattern.name))
        else:
            map_files.append(pattern)

    map_content = read_maps(map_files)

    if not map_content:
        print("Error: No map content to insert", file=sys.stderr)
        sys.exit(1)

    token_estimate = count_tokens_estimate(map_content)
    print(f"Map content: ~{token_estimate} tokens estimated")

    if args.dry_run:
        print(f"\nWould update {args.claude_md}")
        print(f"Insert after: '{args.insert_after}'")
        print(f"\n--- Preview ---\n")
        print(START_MARKER)
        print(map_content[:500] + "..." if len(map_content) > 500 else map_content)
        print(END_MARKER)
        return

    success = update_claude_md(args.claude_md, map_content, args.insert_after)
    if not success:
        sys.exit(1)

    print(f"\nSuccess! CLAUDE.md updated with ~{token_estimate} tokens of architecture map.")
    print(f"Re-run this script anytime to refresh the map.")


if __name__ == '__main__':
    main()
