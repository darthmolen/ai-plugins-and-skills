---
name: python-quality-developer
description: Enforce Python coding standards including ruff rules, pyright type safety, avoiding Any, consistent return types, proper HTTP error signaling, exception chaining, async-first patterns, typed dicts for structured data, and parameter ordering. Use when writing Python code, reviewing .py files, fixing linter violations, or ensuring code quality compliance.
metadata:
  category: language-helpers
---
# Python Quality Standards

## Pre-Coding Checklist

Before writing ANY Python code, check these rules to avoid rework:

### 1. Type Safety — Avoid `Any`

**`Any` is a type escape hatch, not a type.** Every `Any` disables type checking for everything it touches.

```python
# WRONG: Any infects everything it touches
def process(data: Any) -> Any:
    return data["key"]  # no type error even if data is an int

# CORRECT: Use specific types, Protocol, or TypeVar
def process(data: dict[str, str]) -> str:
    return data["key"]

# ACCEPTABLE: Third-party SDK boundary you don't control
def create_session(client: Any) -> None:  # SDK has no type stubs
    ...
```

**When `Any` is acceptable:**
- SDK/library boundaries with no type stubs (annotate with comment)
- `TypedDict` fields that hold non-serializable objects from external code

**When `Any` is NOT acceptable:**
- Function parameters you define
- Return types you control
- Local variables
- Collection element types (`list[Any]`, `dict[str, Any]`)

### 2. Type All Structured Dicts

If a dict has known keys accessed in multiple places, define a `TypedDict`:

```python
# WRONG: Untyped dict — typos pass silently
store: dict[str, dict] = {}
state["pahse"]  # no error

# CORRECT: TypedDict catches key errors at type-check time
class _SwarmStateRequired(TypedDict):
    swarm_id: str
    phase: str
    round_number: int

class SwarmState(_SwarmStateRequired, total=False):
    orchestrator: Any  # SDK object, no stubs — acceptable Any
    report: str | None
```

Use `TypedDict` (not Pydantic) when the dict holds non-serializable objects. Use Pydantic for API request/response schemas.

### 3. One Function, One Return Type

**Never return different types from the same function.** Callers should not need `isinstance` checks.

```python
# WRONG: Returns str OR None OR dict depending on path
def get_result(id: str) -> str | None | dict:
    ...

# CORRECT: One type, use exceptions for errors
def get_result(id: str) -> str:
    if not found:
        raise KeyError(f"Not found: {id}")
    return result
```

### 4. HTTP Error Signaling — Raise, Don't Return

**This applies to any HTTP framework** (FastAPI, Flask, Django, Starlette, etc.). Errors are exceptions, not alternative return values.

```python
# WRONG: Returns 200 with error body — clients must inspect body to detect failure
@app.get("/items/{id}")
async def get_item(id: str) -> dict:
    item = db.get(id)
    if not item:
        return {"error": "Not found", "status": "failed"}  # 200 OK!
    return {"item": item}

# ALSO WRONG: Returns a different Response type — callers must type-switch
async def update_file(key: str) -> dict:
    if not valid:
        return JSONResponse(status_code=422, content={"errors": [...]})
    return {"valid": True}

# CORRECT: Raise framework exception, return only success
async def get_item(id: str) -> dict:
    item = db.get(id)
    if not item:
        raise HTTPException(status_code=404, detail="Not found")
    return {"item": item}

async def update_file(key: str) -> dict:
    if not valid:
        raise HTTPException(status_code=422, detail={"errors": [...]})
    return {"valid": True}
```

**The principle:** HTTP status codes exist to signal errors. Use them. A 200 response with `{"error": "..."}` defeats every HTTP client, middleware, and monitoring tool.

### 5. Async-First — Never Block the Event Loop

Python's `asyncio` runs on a **single thread**. A blocking call freezes every concurrent request.

```python
# WRONG: Blocks the event loop — all other requests stall
import requests
resp = requests.get("https://api.example.com")  # sync HTTP in async context

import time
time.sleep(5)  # blocks entire server for 5 seconds

data = open("big_file.csv").read()  # sync I/O blocks

# CORRECT: Use async equivalents
import httpx
async with httpx.AsyncClient() as client:
    resp = await client.get("https://api.example.com")

await asyncio.sleep(5)  # yields control to other coroutines

import aiofiles
async with aiofiles.open("big_file.csv") as f:
    data = await f.read()
```

**When you must call sync code from async context**, offload to a thread:

```python
import asyncio

# CPU-bound or unavoidable sync library
result = await asyncio.to_thread(sync_heavy_function, arg1, arg2)
```

**Red flags for blocking calls in async code:**
- `requests.*` (use `httpx` async client)
- `time.sleep()` (use `asyncio.sleep()`)
- `open().read()` for large files (use `aiofiles` or `asyncio.to_thread`)
- `subprocess.run()` (use `asyncio.create_subprocess_exec`)
- Any database driver without `async` prefix (use `asyncpg`, `aiosqlite`, etc.)

### 6. Exception Handling

**Always chain exceptions** with `from`:

```python
# WRONG: Swallows original traceback (B904)
except ValueError:
    raise HTTPException(status_code=400, detail="Bad input")

# CORRECT: Preserves cause chain
except ValueError as exc:
    raise HTTPException(status_code=400, detail="Bad input") from exc
```

**Never bare `except: pass`** without justification:

```python
# WRONG: Silent failure (S110)
except Exception:
    pass

# CORRECT: Explicit suppression with reason
with contextlib.suppress(OSError):
    data = file.read_text()  # skip unreadable files
```

### 7. Parameter Ordering Convention

When multiple functions share a parameter, it goes in the **same ordinal position** across all functions:

```python
# WRONG: swarm_id wanders between positions
async def get_detail(task_id: str, swarm_id: str | None = None): ...
async def read_file(path: str, swarm_id: str | None = None): ...

# CORRECT: Shared parameter always first
async def get_detail(swarm_id: str | None = None, task_id: str = ""): ...
async def read_file(swarm_id: str | None = None, path: str = ""): ...
```

### 8. Data Freshness

**Never cache mutable state in a parallel dict.** Query the authoritative source:

```python
# WRONG: Stale copy — initialized once, never updated
state = {"tasks": [], "agents": []}  # always empty

# CORRECT: Query live source at read time
tasks = await service.task_board.get_tasks()
```

### 9. Import Hygiene

- Imports sorted: stdlib > third-party > local (ruff `I001`)
- Remove unused imports immediately (ruff `F401`)
- No `from module import *`

### 10. Path Safety

Guard against path traversal when reading user-specified paths:

```python
target = (base_dir / user_path).resolve()
if not str(target).startswith(str(base_dir.resolve())):
    raise ValueError("Path traversal not allowed")
```

## Quick Reference

| Rule | Description | Enforced By |
|------|-------------|-------------|
| Avoid `Any` | Use specific types; `Any` only at SDK boundaries | pyright |
| Type structured dicts | `TypedDict` for known-key dicts | pyright |
| One return type | Never return different types from same function | pyright |
| Raise for HTTP errors | Use status codes and exceptions, not 200+error body | code review |
| Async-first | Never `requests`, `time.sleep`, sync I/O in async code | ruff, code review |
| Chain exceptions | `raise X from exc` in except blocks | ruff B904 |
| No silent pass | `contextlib.suppress` or comment justification | ruff S110 |
| Sort imports | stdlib > third-party > local | ruff I001 |
| Consistent param order | Shared params in same position across functions | code review |
| No stale state | Query authoritative source at read time | code review |

## Before Submitting Code

```bash
ruff check src/ tests/           # Zero violations
ruff format --check src/ tests/  # Already formatted
pyright src/                     # Zero errors
pytest tests/ -v                 # All passing
```

## Red Flags — STOP and Fix

- `Any` on a type you control — use a real type
- `dict[str, dict]` with keys accessed by string literals — needs `TypedDict`
- Function returns both a model and a Response — raise exception instead
- 200 response with `{"error": "..."}` — use proper HTTP status code
- `requests.get()` or `time.sleep()` in async function — use async equivalent
- `except SomeError:` without `from exc` — chain it
- `except Exception: pass` without comment — justify or suppress properly
- Same parameter in different positions across related functions — standardize
- Initialized-but-never-updated lists returned by API — query live data
