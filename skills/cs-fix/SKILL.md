---
name: cs-fix
description: Fix build errors, StyleCop violations, or test failures in C# code. Use when the build is broken, tests are failing, or linting errors need resolving.
context: fork
agent: Explore
allowed-tools: Read Grep Glob Bash
---

## Current Project State

**Build output:**
!`dotnet build 2>&1 | tail -50`

**StyleCop violations:**
!`dotnet build 2>&1 | grep -E "warning SA|error SA" | head -30`

**Failing tests:**
!`dotnet test --no-build 2>&1 | grep -E "Failed|Error" | head -20`

**Recent changes:**
!`git diff --stat HEAD`

**Modified files (full diff):**
!`git diff HEAD -- "*.cs" | head -200`

## Instructions

Using the injected state above:

1. **Identify the root cause** — don't fix symptoms independently
2. **Fix errors in dependency order** (compilation errors before StyleCop)
3. **Verify each fix** doesn't introduce new violations
4. **Apply the three-iteration quality check** before finishing:
   - Iteration 1: Fix all identified issues based on error messages
   - Iteration 2: Re-run build, fix any remaining or newly introduced issues
   - Iteration 3: Final verification pass
   - If issues persist after 3 iterations: STOP and report remaining issues to the user

### Fix Priority Order

1. Missing `using` directives and namespace issues (CS0246, CS0234)
2. Compilation errors (CS-prefixed errors)
3. StyleCop violations in dependency order:
   - File-level: SA1633 (headers), SA1210 (using order), SA1402/SA1649 (file naming)
   - Type-level: SA1201 (element order), SA1309 (field naming)
   - Member-level: SA1101 (this. prefix), SA1116 (parameter placement)
   - Formatting: SA1028 (trailing whitespace), SA1518 (EOF newline), SA1500/SA1505/SA1513 (braces)
   - Documentation: SA1614, SA1616, SA1623, SA1629
4. Analyzer warnings (CA-prefixed, IDE-prefixed)
5. Test failures

### Important

- Do NOT use `dotnet format` — it can remove necessary usings and break source-generated code (LoggerMessage patterns)
- Always use `this.` prefix for instance member access
- Field names: camelCase without underscore prefix (`logger` not `_logger`)
- File endings: CRLF for .cs files, single newline at EOF
- Refer to the `csharp-quality-developer` skill for detailed rule explanations
