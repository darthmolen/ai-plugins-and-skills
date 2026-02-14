#!/usr/bin/env python3
"""
C# Codebase Structure Mapper

Uses regex-based parsing to extract structural information from C# codebases.
Outputs a token-efficient architecture map suitable for CLAUDE.md.

Note: This is regex-based for portability (no Roslyn dependency).
Handles 90%+ of common C# patterns correctly.

Usage:
    python map_csharp.py --root ./src --output /tmp/map_csharp.md
    python map_csharp.py --root ./src --depth classes --exclude Tests,Migrations
"""

import argparse
import re
import sys
from pathlib import Path
from collections import defaultdict
from typing import NamedTuple


class MemberInfo(NamedTuple):
    name: str
    kind: str  # 'method', 'property', 'field'
    is_async: bool = False


class TypeInfo(NamedTuple):
    name: str
    kind: str  # 'class', 'interface', 'enum', 'record', 'struct'
    bases: list[str]
    members: list[MemberInfo]
    attributes: list[str]
    is_partial: bool


class FileInfo(NamedTuple):
    path: str
    namespace: str
    types: list[TypeInfo]
    usings: list[str]


DEFAULT_EXCLUDES = {
    'bin', 'obj', '.git', 'node_modules', 'packages',
    'TestResults', '.vs', 'Debug', 'Release'
}

FRAMEWORK_INDICATORS = {
    'Microsoft.AspNetCore': 'ASP.NET Core',
    'Microsoft.EntityFrameworkCore': 'EF Core',
    'System.Text.Json': 'System.Text.Json',
    'Newtonsoft.Json': 'Newtonsoft.Json',
    'MediatR': 'MediatR',
    'FluentValidation': 'FluentValidation',
    'Serilog': 'Serilog',
    'AutoMapper': 'AutoMapper',
    'Dapper': 'Dapper',
    'xunit': 'xUnit',
    'NUnit': 'NUnit',
    'Moq': 'Moq',
}

# Regex patterns
NAMESPACE_PATTERN = re.compile(
    r'^\s*namespace\s+([\w.]+)\s*[;{]',
    re.MULTILINE
)

USING_PATTERN = re.compile(
    r'^\s*using\s+(?:static\s+)?([\w.]+)\s*;',
    re.MULTILINE
)

TYPE_PATTERN = re.compile(
    r'''
    (?P<attributes>(?:\[[\w\s,()="\.]+\]\s*)*)  # Attributes
    (?P<modifiers>(?:public|private|protected|internal|static|abstract|sealed|partial)\s+)*
    (?P<kind>class|interface|enum|record|struct)\s+
    (?P<name>\w+)
    (?:<[^>]+>)?  # Generic parameters
    (?:\s*:\s*(?P<bases>[^{]+))?  # Base types
    \s*\{
    ''',
    re.VERBOSE | re.MULTILINE
)

METHOD_PATTERN = re.compile(
    r'''
    (?P<attributes>(?:\[[\w\s,()="\.]+\]\s*)*)  # Attributes
    (?P<modifiers>(?:public|private|protected|internal|static|virtual|override|abstract|async)\s+)*
    (?P<return>[\w<>\[\],\s\?]+)\s+
    (?P<name>\w+)\s*
    (?:<[^>]+>)?  # Generic parameters
    \((?P<params>[^)]*)\)  # Parameters
    ''',
    re.VERBOSE
)

PROPERTY_PATTERN = re.compile(
    r'''
    (?P<attributes>(?:\[[\w\s,()="\.]+\]\s*)*)  # Attributes
    (?P<modifiers>(?:public|private|protected|internal|static|virtual|override|abstract)\s+)*
    (?P<type>[\w<>\[\],\s\?]+)\s+
    (?P<name>\w+)\s*
    \{\s*(?:get|set|init)
    ''',
    re.VERBOSE
)


def should_exclude(path: Path, excludes: set[str]) -> bool:
    """Check if path should be excluded."""
    for part in path.parts:
        if part in excludes or part.startswith('.'):
            return True
    return False


def extract_attributes(attr_str: str) -> list[str]:
    """Extract attribute names from attribute string."""
    if not attr_str:
        return []
    attrs = re.findall(r'\[(\w+)', attr_str)
    return attrs


def find_type_end(content: str, start: int) -> int:
    """Find the end of a type definition by matching braces."""
    depth = 0
    i = start
    while i < len(content):
        if content[i] == '{':
            depth += 1
        elif content[i] == '}':
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return len(content)


def parse_type(match: re.Match, content: str, depth: str) -> TypeInfo:
    """Parse a type definition from regex match."""
    modifiers = match.group('modifiers') or ''
    is_partial = 'partial' in modifiers

    kind = match.group('kind')
    name = match.group('name')

    bases = []
    bases_str = match.group('bases')
    if bases_str:
        # Split on comma, clean up whitespace and where clauses
        bases_str = re.sub(r'\bwhere\b.*', '', bases_str)
        for base in bases_str.split(','):
            base = base.strip()
            if base:
                # Remove generic parameters for brevity
                base = re.sub(r'<[^>]+>', '', base)
                bases.append(base)

    attributes = extract_attributes(match.group('attributes'))

    members = []
    if depth != 'classes' and kind != 'enum':
        # Find the body of this type
        type_start = match.end()
        type_end = find_type_end(content, match.start())
        type_body = content[type_start:type_end]

        # Find methods
        for m in METHOD_PATTERN.finditer(type_body):
            method_name = m.group('name')
            method_modifiers = m.group('modifiers') or ''

            # Skip private/protected unless full depth
            if depth != 'full':
                if 'private' in method_modifiers or 'protected' in method_modifiers:
                    continue

            # Skip constructors, getters/setters
            if method_name in [name, 'get', 'set', 'add', 'remove']:
                continue

            is_async = 'async' in method_modifiers
            members.append(MemberInfo(
                name=f"{method_name}()",
                kind='method',
                is_async=is_async
            ))

        # Find properties
        for p in PROPERTY_PATTERN.finditer(type_body):
            prop_modifiers = p.group('modifiers') or ''
            if depth != 'full':
                if 'private' in prop_modifiers or 'protected' in prop_modifiers:
                    continue

            members.append(MemberInfo(
                name=p.group('name'),
                kind='property'
            ))

    return TypeInfo(
        name=name,
        kind=kind,
        bases=bases,
        members=members,
        attributes=attributes,
        is_partial=is_partial
    )


def parse_file(filepath: Path, depth: str) -> FileInfo | None:
    """Parse a C# file and extract structure."""
    try:
        content = filepath.read_text(encoding='utf-8-sig')  # Handle BOM
    except UnicodeDecodeError:
        try:
            content = filepath.read_text(encoding='latin-1')
        except Exception:
            print(f"Warning: Skipping unreadable file: {filepath}", file=sys.stderr)
            return None

    # Extract namespace
    ns_match = NAMESPACE_PATTERN.search(content)
    namespace = ns_match.group(1) if ns_match else ''

    # Extract usings
    usings = [m.group(1) for m in USING_PATTERN.finditer(content)]

    # Extract types
    types = []
    for match in TYPE_PATTERN.finditer(content):
        type_info = parse_type(match, content, depth)
        types.append(type_info)

    return FileInfo(
        path=str(filepath),
        namespace=namespace,
        types=types,
        usings=usings
    )


def detect_frameworks(all_usings: set[str]) -> list[str]:
    """Detect frameworks from using statements."""
    detected = []
    for using, name in FRAMEWORK_INDICATORS.items():
        for u in all_usings:
            if u.startswith(using):
                if name not in detected:
                    detected.append(name)
                break
    return detected


def format_type(t: TypeInfo) -> str:
    """Format a type for output."""
    parts = [f"`{t.name}"]

    if t.bases:
        parts[0] += f" : {', '.join(t.bases[:3])}"
        if len(t.bases) > 3:
            parts[0] += f' (+{len(t.bases) - 3})'

    parts[0] += '`'

    if t.kind != 'class':
        parts.append(f'[{t.kind}]')

    # Key attributes
    important_attrs = ['ApiController', 'Authorize', 'Route', 'Injectable', 'Component']
    shown_attrs = [a for a in t.attributes if a in important_attrs]
    if shown_attrs:
        parts.append(f"[{', '.join(shown_attrs)}]")

    # Methods
    methods = [m.name for m in t.members if m.kind == 'method']
    if methods:
        shown = ', '.join(methods[:5])
        if len(methods) > 5:
            shown += f' (+{len(methods) - 5} more)'
        parts.append('→ ' + shown)

    return ' '.join(parts)


def format_output(files: list[FileInfo], root: Path, frameworks: list[str]) -> str:
    """Format the parsed files into markdown output."""
    lines = []
    lines.append("### C#: " + str(root.name) + "/")

    if frameworks:
        lines.append(f"**Frameworks detected:** {', '.join(frameworks)}")
        lines.append("")

    # Group by namespace/project
    ns_files: dict[str, list[FileInfo]] = defaultdict(list)
    for f in files:
        ns = f.namespace or '(no namespace)'
        ns_files[ns].append(f)

    for ns in sorted(ns_files.keys()):
        ns_file_list = ns_files[ns]
        lines.append(f"- `{ns}`")

        for f in sorted(ns_file_list, key=lambda x: x.path):
            rel_path = Path(f.path).relative_to(root)
            filename = rel_path.name

            if not f.types:
                continue

            for t in f.types:
                lines.append(f"  - `{filename}`: {format_type(t)}")

    return '\n'.join(lines)


def main():
    parser = argparse.ArgumentParser(description='Map C# codebase structure')
    parser.add_argument('--root', type=Path, required=True, help='Root directory to scan')
    parser.add_argument('--output', type=Path, help='Output file (default: stdout)')
    parser.add_argument('--depth', choices=['classes', 'methods', 'full'], default='methods',
                        help='Output depth level')
    parser.add_argument('--exclude', type=str, default='',
                        help='Comma-separated directories to exclude')
    args = parser.parse_args()

    root = args.root.resolve()
    if not root.exists():
        print(f"Error: Root directory does not exist: {root}", file=sys.stderr)
        sys.exit(1)

    excludes = DEFAULT_EXCLUDES.copy()
    if args.exclude:
        excludes.update(args.exclude.split(','))

    # Collect all C# files
    files: list[FileInfo] = []
    all_usings: set[str] = set()
    file_count = 0
    type_count = 0
    member_count = 0

    for cs_file in root.rglob('*.cs'):
        rel_path = cs_file.relative_to(root)
        if should_exclude(rel_path, excludes):
            continue

        file_info = parse_file(cs_file, args.depth)
        if file_info:
            files.append(file_info)
            all_usings.update(file_info.usings)
            file_count += 1
            type_count += len(file_info.types)
            for t in file_info.types:
                member_count += len(t.members)

    frameworks = detect_frameworks(all_usings)
    output = format_output(files, root, frameworks)

    # Add stats as comment
    stats = f"\n<!-- C#: {file_count} files, {type_count} types, {member_count} members -->\n"

    if args.output:
        args.output.write_text(output + stats, encoding='utf-8')
        print(f"Output written to {args.output}")
        print(f"Files: {file_count}, Types: {type_count}, Members: {member_count}")
    else:
        print(output + stats)


if __name__ == '__main__':
    main()
