# Python error handling

## 7. Error Handling

### Custom Exception Hierarchy

```python
class AppError(Exception):
    """Base application error"""
    def __init__(self, message: str, code: str = "UNKNOWN"):
        self.message = message
        self.code = code
        super().__init__(message)

class NotFoundError(AppError):
    def __init__(self, resource: str, id: int):
        super().__init__(f"{resource} with id {id} not found", "NOT_FOUND")

class ValidationError(AppError):
    def __init__(self, field: str, message: str):
        super().__init__(message, f"VALIDATION_{field.upper()}")
```

### Result Pattern

```python
from dataclasses import dataclass
from typing import Generic, TypeVar

T = TypeVar('T')
E = TypeVar('E')

@dataclass
class Ok(Generic[T]):
    value: T

@dataclass
class Err(Generic[E]):
    error: E

Result = Ok[T] | Err[E]

async def get_user(user_id: int) -> Result[User, str]:
    user = await db.get(User, user_id)
    if user is None:
        return Err("User not found")
    return Ok(user)

# Usage
match await get_user(123):
    case Ok(user):
        print(f"Found: {user.name}")
    case Err(error):
        print(f"Error: {error}")
```
