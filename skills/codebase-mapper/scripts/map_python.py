#!/usr/bin/env python3
"""
Python Codebase Structure Mapper

Uses Python's ast module to extract structural information from Python codebases.
Outputs a token-efficient architecture map suitable for CLAUDE.md.

Usage:
    python map_python.py --root ./src --output /tmp/map_python.md
    python map_python.py --root ./src --depth classes --exclude tests,migrations
"""

import ast
import argparse
import os
import sys
from pathlib import Path
from collections import defaultdict
from typing import NamedTuple


class ClassInfo(NamedTuple):
    name: str
    bases: list[str]
    methods: list[str]
    decorators: list[str]
    properties: list[str]
    is_dataclass: bool


class ModuleInfo(NamedTuple):
    path: str
    classes: list[ClassInfo]
    functions: list[str]
    imports: list[str]


DEFAULT_EXCLUDES = {
    '__pycache__', '.git', 'venv', '.venv', 'env', '.env',
    'node_modules', 'build', 'dist', '.tox', '.pytest_cache',
    '.mypy_cache', 'eggs', '*.egg-info', '.eggs'
}

FRAMEWORK_INDICATORS = {
    'fastapi': 'FastAPI',
    'flask': 'Flask',
    'django': 'Django',
    'sqlalchemy': 'SQLAlchemy',
    'pydantic': 'Pydantic',
    'pytest': 'pytest',
    'celery': 'Celery',
    'httpx': 'httpx',
    'aiohttp': 'aiohttp',
}


def should_exclude(path: Path, excludes: set[str]) -> bool:
    """Check if path should be excluded."""
    for part in path.parts:
        if part in excludes or part.startswith('.'):
            return True
    return False


def extract_decorator_name(decorator: ast.expr) -> str:
    """Extract decorator name from AST node."""
    if isinstance(decorator, ast.Name):
        return decorator.id
    elif isinstance(decorator, ast.Attribute):
        parts = []
        node = decorator
        while isinstance(node, ast.Attribute):
            parts.append(node.attr)
            node = node.value
        if isinstance(node, ast.Name):
            parts.append(node.id)
        return '.'.join(reversed(parts))
    elif isinstance(decorator, ast.Call):
        return extract_decorator_name(decorator.func)
    return '?'


def parse_class(node: ast.ClassDef, depth: str) -> ClassInfo:
    """Extract class information from AST node."""
    bases = []
    for base in node.bases:
        if isinstance(base, ast.Name):
            bases.append(base.id)
        elif isinstance(base, ast.Attribute):
            bases.append(base.attr)

    decorators = [extract_decorator_name(d) for d in node.decorator_list]
    is_dataclass = 'dataclass' in decorators

    methods = []
    properties = []

    if depth != 'classes':
        for item in node.body:
            if isinstance(item, ast.FunctionDef) or isinstance(item, ast.AsyncFunctionDef):
                # Skip private methods unless full depth
                if depth != 'full' and item.name.startswith('_') and not item.name.startswith('__'):
                    continue
                # Skip dunder methods except __init__
                if item.name.startswith('__') and item.name != '__init__':
                    continue

                item_decorators = [extract_decorator_name(d) for d in item.decorator_list]
                if 'property' in item_decorators:
                    properties.append(item.name)
                else:
                    prefix = 'async ' if isinstance(item, ast.AsyncFunctionDef) else ''
                    methods.append(f"{prefix}{item.name}()")

    return ClassInfo(
        name=node.name,
        bases=bases,
        methods=methods,
        decorators=decorators,
        properties=properties,
        is_dataclass=is_dataclass,
    )


def parse_module(filepath: Path, depth: str) -> ModuleInfo | None:
    """Parse a Python module and extract structure."""
    try:
        content = filepath.read_text(encoding='utf-8')
    except UnicodeDecodeError:
        print(f"Warning: Skipping non-UTF8 file: {filepath}", file=sys.stderr)
        return None

    try:
        tree = ast.parse(content, filename=str(filepath))
    except SyntaxError as e:
        print(f"Warning: Syntax error in {filepath}: {e}", file=sys.stderr)
        return None

    classes = []
    functions = []
    imports = []

    for node in ast.walk(tree):
        if isinstance(node, ast.ClassDef):
            classes.append(parse_class(node, depth))
        elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            # Only top-level functions
            if node.col_offset == 0:
                if depth != 'classes':
                    if not node.name.startswith('_') or depth == 'full':
                        prefix = 'async ' if isinstance(node, ast.AsyncFunctionDef) else ''
                        functions.append(f"{prefix}{node.name}()")
        elif isinstance(node, ast.Import):
            for alias in node.names:
                imports.append(alias.name.split('.')[0])
        elif isinstance(node, ast.ImportFrom):
            if node.module:
                imports.append(node.module.split('.')[0])

    return ModuleInfo(
        path=str(filepath),
        classes=classes,
        functions=functions,
        imports=list(set(imports)),
    )


def detect_frameworks(all_imports: set[str]) -> list[str]:
    """Detect frameworks from imports."""
    detected = []
    for imp, name in FRAMEWORK_INDICATORS.items():
        if imp in all_imports:
            detected.append(name)
    return detected


def format_class(cls: ClassInfo) -> str:
    """Format a class for output."""
    parts = [f"`{cls.name}"]
    if cls.bases:
        parts[0] += f"({', '.join(cls.bases)})"
    parts[0] += '`'

    if cls.is_dataclass:
        parts.append('[dataclass]')

    if cls.methods:
        parts.append('→ ' + ', '.join(cls.methods[:5]))
        if len(cls.methods) > 5:
            parts.append(f'(+{len(cls.methods) - 5} more)')

    return ' '.join(parts)


def format_output(modules: dict[str, list[ModuleInfo]], root: Path, frameworks: list[str]) -> str:
    """Format the parsed modules into markdown output."""
    lines = []
    lines.append("### Python: " + str(root.name) + "/")

    if frameworks:
        lines.append(f"**Frameworks detected:** {', '.join(frameworks)}")
        lines.append("")

    # Group by directory
    dir_modules: dict[str, list[ModuleInfo]] = defaultdict(list)
    for path, mods in modules.items():
        for mod in mods:
            rel_path = Path(mod.path).relative_to(root)
            dir_name = str(rel_path.parent) if rel_path.parent != Path('.') else ''
            dir_modules[dir_name].append(mod)

    # Sort directories
    for dir_name in sorted(dir_modules.keys()):
        mods = dir_modules[dir_name]
        if dir_name:
            lines.append(f"- `{dir_name}/`")
            indent = "  "
        else:
            indent = ""

        for mod in sorted(mods, key=lambda m: m.path):
            rel_path = Path(mod.path).relative_to(root)
            filename = rel_path.name

            # Skip __init__.py unless it has meaningful content
            if filename == '__init__.py' and not mod.classes and not mod.functions:
                continue

            mod_parts = [f"{indent}- `{filename}`:"]

            if mod.classes:
                for cls in mod.classes:
                    mod_parts.append(f"{indent}  - {format_class(cls)}")

            if mod.functions:
                funcs = ', '.join(mod.functions[:5])
                if len(mod.functions) > 5:
                    funcs += f' (+{len(mod.functions) - 5} more)'
                mod_parts.append(f"{indent}  - Functions: {funcs}")

            if len(mod_parts) > 1:
                lines.extend(mod_parts)

    return '\n'.join(lines)


def main():
    parser = argparse.ArgumentParser(description='Map Python codebase structure')
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

    # Collect all Python files
    modules: dict[str, list[ModuleInfo]] = defaultdict(list)
    all_imports: set[str] = set()
    file_count = 0
    class_count = 0
    method_count = 0

    for py_file in root.rglob('*.py'):
        rel_path = py_file.relative_to(root)
        if should_exclude(rel_path, excludes):
            continue

        module = parse_module(py_file, args.depth)
        if module:
            modules[str(py_file.parent)].append(module)
            all_imports.update(module.imports)
            file_count += 1
            class_count += len(module.classes)
            for cls in module.classes:
                method_count += len(cls.methods)

    frameworks = detect_frameworks(all_imports)
    output = format_output(modules, root, frameworks)

    # Add stats as comment
    stats = f"\n<!-- Python: {file_count} files, {class_count} classes, {method_count} methods -->\n"

    if args.output:
        args.output.write_text(output + stats, encoding='utf-8')
        print(f"Output written to {args.output}")
        print(f"Files: {file_count}, Classes: {class_count}, Methods: {method_count}")
    else:
        print(output + stats)


if __name__ == '__main__':
    main()
