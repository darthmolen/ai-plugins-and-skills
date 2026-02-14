#!/usr/bin/env python3
"""
TypeScript/JavaScript Codebase Structure Mapper

Uses regex-based parsing to extract structural information from TypeScript codebases.
Outputs a token-efficient architecture map suitable for CLAUDE.md.

Handles:
- Classes with inheritance
- Interfaces and types
- Functions (exported and module-level)
- React components (function and class-based)
- Angular decorators

Usage:
    python map_typescript.py --root ./src --output /tmp/map_typescript.md
    python map_typescript.py --root ./src --depth classes --exclude tests,__tests__
"""

import argparse
import re
import sys
from pathlib import Path
from collections import defaultdict
from typing import NamedTuple


class MemberInfo(NamedTuple):
    name: str
    kind: str  # 'method', 'property', 'function'
    is_async: bool = False


class TypeInfo(NamedTuple):
    name: str
    kind: str  # 'class', 'interface', 'type', 'enum', 'function', 'component'
    bases: list[str]
    members: list[MemberInfo]
    decorators: list[str]
    is_exported: bool


class FileInfo(NamedTuple):
    path: str
    types: list[TypeInfo]
    imports: list[str]


DEFAULT_EXCLUDES = {
    'node_modules', 'dist', 'build', '.git', 'coverage',
    '__tests__', '__mocks__', '.next', '.nuxt', '.output'
}

FRAMEWORK_INDICATORS = {
    'react': 'React',
    '@angular': 'Angular',
    'vue': 'Vue',
    'next': 'Next.js',
    'express': 'Express',
    'fastify': 'Fastify',
    'nestjs': 'NestJS',
    '@nestjs': 'NestJS',
    'typeorm': 'TypeORM',
    'prisma': 'Prisma',
    'zod': 'Zod',
    'trpc': 'tRPC',
    '@trpc': 'tRPC',
}

# Regex patterns
IMPORT_PATTERN = re.compile(
    r'''import\s+(?:
        (?:type\s+)?
        (?:\{[^}]+\}|\*\s+as\s+\w+|\w+)
        \s+from\s+
    )?['"]([^'"]+)['"]''',
    re.VERBOSE
)

CLASS_PATTERN = re.compile(
    r'''
    (?P<decorators>(?:@\w+(?:\([^)]*\))?\s*)*)  # Decorators
    (?P<export>export\s+)?
    (?P<abstract>abstract\s+)?
    class\s+
    (?P<name>\w+)
    (?:<[^>]+>)?  # Generic parameters
    (?:\s+extends\s+(?P<extends>[\w<>,.]+))?
    (?:\s+implements\s+(?P<implements>[\w<>,.]+))?
    \s*\{
    ''',
    re.VERBOSE
)

INTERFACE_PATTERN = re.compile(
    r'''
    (?P<export>export\s+)?
    interface\s+
    (?P<name>\w+)
    (?:<[^>]+>)?
    (?:\s+extends\s+(?P<extends>[\w<>,.]+))?
    \s*\{
    ''',
    re.VERBOSE
)

TYPE_ALIAS_PATTERN = re.compile(
    r'''
    (?P<export>export\s+)?
    type\s+
    (?P<name>\w+)
    (?:<[^>]+>)?
    \s*=
    ''',
    re.VERBOSE
)

ENUM_PATTERN = re.compile(
    r'''
    (?P<export>export\s+)?
    (?:const\s+)?
    enum\s+
    (?P<name>\w+)
    \s*\{
    ''',
    re.VERBOSE
)

FUNCTION_PATTERN = re.compile(
    r'''
    (?P<export>export\s+)?
    (?P<default>default\s+)?
    (?P<async>async\s+)?
    function\s+
    (?P<name>\w+)
    (?:<[^>]+>)?
    \s*\(
    ''',
    re.VERBOSE
)

ARROW_FUNCTION_PATTERN = re.compile(
    r'''
    (?P<export>export\s+)?
    (?:const|let)\s+
    (?P<name>\w+)
    (?::\s*[\w<>|()\s,=>\[\]]+)?\s*=\s*
    (?P<async>async\s+)?
    (?:\([^)]*\)|[\w]+)\s*(?::\s*[\w<>|\s]+)?\s*=>
    ''',
    re.VERBOSE
)

# React component patterns
REACT_FC_PATTERN = re.compile(
    r'''
    (?P<export>export\s+)?
    (?:const|let)\s+
    (?P<name>\w+)
    \s*:\s*
    (?:React\.)?(?:FC|FunctionComponent|VFC)
    (?:<[^>]+>)?
    ''',
    re.VERBOSE
)

METHOD_PATTERN = re.compile(
    r'''
    (?P<modifier>public|private|protected|static|async|\s)*
    (?P<name>\w+)
    (?:<[^>]+>)?
    \s*\([^)]*\)
    (?:\s*:\s*[\w<>|\s\[\]]+)?
    \s*\{
    ''',
    re.VERBOSE
)


def should_exclude(path: Path, excludes: set[str]) -> bool:
    """Check if path should be excluded."""
    for part in path.parts:
        if part in excludes or part.startswith('.'):
            return True
    return False


def extract_decorators(decorator_str: str) -> list[str]:
    """Extract decorator names from decorator string."""
    if not decorator_str:
        return []
    return re.findall(r'@(\w+)', decorator_str)


def find_block_end(content: str, start: int) -> int:
    """Find the end of a block by matching braces."""
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


def is_react_component(name: str, content: str) -> bool:
    """Check if a function looks like a React component."""
    # Component names are PascalCase
    if not name[0].isupper():
        return False
    # Check for JSX return
    if re.search(rf'{name}[^{{]*\{{[^}}]*return\s*\([^)]*<', content):
        return True
    if re.search(rf'{name}[^{{]*\{{[^}}]*return\s*<', content):
        return True
    return False


def parse_class(match: re.Match, content: str, depth: str) -> TypeInfo:
    """Parse a class from regex match."""
    decorators = extract_decorators(match.group('decorators') or '')
    is_exported = bool(match.group('export'))
    name = match.group('name')

    bases = []
    if match.group('extends'):
        bases.append(match.group('extends').split('<')[0].strip())
    if match.group('implements'):
        for impl in match.group('implements').split(','):
            impl = impl.split('<')[0].strip()
            if impl:
                bases.append(impl)

    # Determine kind
    kind = 'class'
    if 'Component' in decorators:
        kind = 'component'
    elif 'Injectable' in decorators:
        kind = 'service'

    members = []
    if depth != 'classes':
        # Find class body
        class_start = match.end()
        class_end = find_block_end(content, match.start())
        class_body = content[class_start:class_end]

        for m in METHOD_PATTERN.finditer(class_body):
            method_name = m.group('name')
            modifiers = m.group('modifier') or ''

            # Skip private unless full depth
            if depth != 'full' and 'private' in modifiers:
                continue

            # Skip constructor and lifecycle methods
            if method_name in ['constructor', 'ngOnInit', 'ngOnDestroy', 'componentDidMount',
                               'componentWillUnmount', 'render', 'get', 'set']:
                continue

            is_async = 'async' in modifiers
            members.append(MemberInfo(
                name=f"{method_name}()",
                kind='method',
                is_async=is_async
            ))

    return TypeInfo(
        name=name,
        kind=kind,
        bases=bases,
        members=members,
        decorators=decorators,
        is_exported=is_exported
    )


def parse_file(filepath: Path, depth: str) -> FileInfo | None:
    """Parse a TypeScript file and extract structure."""
    try:
        content = filepath.read_text(encoding='utf-8')
    except UnicodeDecodeError:
        print(f"Warning: Skipping non-UTF8 file: {filepath}", file=sys.stderr)
        return None

    # Remove comments to avoid false matches
    content_no_comments = re.sub(r'//.*$', '', content, flags=re.MULTILINE)
    content_no_comments = re.sub(r'/\*.*?\*/', '', content_no_comments, flags=re.DOTALL)

    # Extract imports
    imports = [m.group(1) for m in IMPORT_PATTERN.finditer(content)]

    types = []

    # Parse classes
    for match in CLASS_PATTERN.finditer(content_no_comments):
        types.append(parse_class(match, content_no_comments, depth))

    # Parse interfaces
    for match in INTERFACE_PATTERN.finditer(content_no_comments):
        types.append(TypeInfo(
            name=match.group('name'),
            kind='interface',
            bases=[match.group('extends')] if match.group('extends') else [],
            members=[],
            decorators=[],
            is_exported=bool(match.group('export'))
        ))

    # Parse type aliases
    for match in TYPE_ALIAS_PATTERN.finditer(content_no_comments):
        types.append(TypeInfo(
            name=match.group('name'),
            kind='type',
            bases=[],
            members=[],
            decorators=[],
            is_exported=bool(match.group('export'))
        ))

    # Parse enums
    for match in ENUM_PATTERN.finditer(content_no_comments):
        types.append(TypeInfo(
            name=match.group('name'),
            kind='enum',
            bases=[],
            members=[],
            decorators=[],
            is_exported=bool(match.group('export'))
        ))

    # Parse functions
    for match in FUNCTION_PATTERN.finditer(content_no_comments):
        name = match.group('name')
        kind = 'component' if is_react_component(name, content_no_comments) else 'function'
        types.append(TypeInfo(
            name=name,
            kind=kind,
            bases=[],
            members=[],
            decorators=[],
            is_exported=bool(match.group('export'))
        ))

    # Parse React FC components
    for match in REACT_FC_PATTERN.finditer(content_no_comments):
        types.append(TypeInfo(
            name=match.group('name'),
            kind='component',
            bases=['FC'],
            members=[],
            decorators=[],
            is_exported=bool(match.group('export'))
        ))

    # Parse arrow functions (only exported ones at module level)
    for match in ARROW_FUNCTION_PATTERN.finditer(content_no_comments):
        if not match.group('export'):
            continue
        name = match.group('name')
        kind = 'component' if is_react_component(name, content_no_comments) else 'function'
        # Avoid duplicates
        if not any(t.name == name for t in types):
            types.append(TypeInfo(
                name=name,
                kind=kind,
                bases=[],
                members=[],
                decorators=[],
                is_exported=True
            ))

    return FileInfo(
        path=str(filepath),
        types=types,
        imports=imports
    )


def detect_frameworks(all_imports: set[str]) -> list[str]:
    """Detect frameworks from imports."""
    detected = []
    for imp, name in FRAMEWORK_INDICATORS.items():
        for i in all_imports:
            if i == imp or i.startswith(imp + '/') or i.startswith(imp + '-'):
                if name not in detected:
                    detected.append(name)
                break
    return detected


def format_type(t: TypeInfo) -> str:
    """Format a type for output."""
    parts = [f"`{t.name}`"]

    if t.kind not in ['class', 'function']:
        parts.append(f"({t.kind})")

    if t.bases:
        bases_str = ', '.join(t.bases[:2])
        if len(t.bases) > 2:
            bases_str += f' (+{len(t.bases) - 2})'
        parts.append(f": {bases_str}")

    if t.decorators:
        shown = ', '.join(t.decorators[:2])
        parts.append(f"[@{shown}]")

    if t.members:
        methods = [m.name for m in t.members if m.kind == 'method']
        if methods:
            shown = ', '.join(methods[:4])
            if len(methods) > 4:
                shown += f' (+{len(methods) - 4})'
            parts.append('→ ' + shown)

    return ' '.join(parts)


def format_output(files: list[FileInfo], root: Path, frameworks: list[str]) -> str:
    """Format the parsed files into markdown output."""
    lines = []
    lines.append("### TypeScript: " + str(root.name) + "/")

    if frameworks:
        lines.append(f"**Frameworks detected:** {', '.join(frameworks)}")
        lines.append("")

    # Group by directory
    dir_files: dict[str, list[FileInfo]] = defaultdict(list)
    for f in files:
        rel_path = Path(f.path).relative_to(root)
        dir_name = str(rel_path.parent) if rel_path.parent != Path('.') else ''
        dir_files[dir_name].append(f)

    for dir_name in sorted(dir_files.keys()):
        dir_file_list = dir_files[dir_name]
        if dir_name:
            lines.append(f"- `{dir_name}/`")
            indent = "  "
        else:
            indent = ""

        for f in sorted(dir_file_list, key=lambda x: x.path):
            rel_path = Path(f.path).relative_to(root)
            filename = rel_path.name

            # Skip index files unless they have meaningful content
            if filename in ['index.ts', 'index.tsx', 'index.js'] and not f.types:
                continue

            exported_types = [t for t in f.types if t.is_exported]
            if not exported_types:
                continue

            lines.append(f"{indent}- `{filename}`:")
            for t in exported_types:
                lines.append(f"{indent}  - {format_type(t)}")

    return '\n'.join(lines)


def main():
    parser = argparse.ArgumentParser(description='Map TypeScript codebase structure')
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

    # Collect all TypeScript/JavaScript files
    files: list[FileInfo] = []
    all_imports: set[str] = set()
    file_count = 0
    type_count = 0

    for pattern in ['*.ts', '*.tsx', '*.js', '*.jsx']:
        for ts_file in root.rglob(pattern):
            # Skip declaration files
            if ts_file.name.endswith('.d.ts'):
                continue

            rel_path = ts_file.relative_to(root)
            if should_exclude(rel_path, excludes):
                continue

            file_info = parse_file(ts_file, args.depth)
            if file_info:
                files.append(file_info)
                all_imports.update(file_info.imports)
                file_count += 1
                type_count += len(file_info.types)

    frameworks = detect_frameworks(all_imports)
    output = format_output(files, root, frameworks)

    # Add stats as comment
    stats = f"\n<!-- TypeScript: {file_count} files, {type_count} exports -->\n"

    if args.output:
        args.output.write_text(output + stats, encoding='utf-8')
        print(f"Output written to {args.output}")
        print(f"Files: {file_count}, Exports: {type_count}")
    else:
        print(output + stats)


if __name__ == '__main__':
    main()
