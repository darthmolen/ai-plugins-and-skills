---
name: mutation-harness
description: Use when seeding a mutant to check whether a test suite actually detects a wrong answer — the MUTATE step of test-filter-development. Seeds one fault, runs any test command, reports which tests died, and always reverts byte-for-byte, including on Ctrl-C or timeout. Inverts the usual exit code, because a mutant nothing kills is a failure. Framework-agnostic via --test-cmd, so it works for .NET, Node, Python or anything else that exits non-zero on failure.
metadata:
  category: tools
---

# Mutation Harness

Seeds one mutant, runs the suite, reports what died, **always reverts**.

This is the mechanism for the MUTATE step in `test-filter-development`. That skill argues why the
step exists — a green suite proves the test detects *absent*, never *wrong*. This is the tool that
performs it without the two things that go wrong when it is done by hand.

## The two guarantees

**1. The revert always happens.** A hand-run mutation that crashes, times out, or is interrupted
between "seed" and "revert" leaves a deliberate bug in the working tree, and the next commit ships
it. In the session that motivated this script, three mutants were run by hand and survived that risk
purely by luck. Here the restore runs in a `finally`, and again on Ctrl-C and on process exit.

**2. A mutant nothing kills is a failure.** The exit code is inverted relative to a normal test run:

| Exit | Meaning |
|---|---|
| `0` | **MUTANT KILLED** — the suite went red, and any `--expect-killed-by` fired |
| `1` | **MUTANT SURVIVED** — the suite passed with a deliberate fault in place |
| `2` | **BUILD-KILLED** — the mutant did not compile; inconclusive |
| `3` | usage or anchor error |
| `4` | **TIMED OUT** — the suite never finished; inconclusive |
| `5` | **COMMAND-FAILED** — the test command never ran; inconclusive |
| `6` | **UNATTRIBUTED** — killed, but `--expect-killed-by` could not be checked |
| `7` | **KILLED BY WRONG TEST** |

`0` is the only success. `2`, `4` and `5` are kept distinct from it on purpose. **A command that
never ran must never be scored as a kill** — that is the worst failure a mutation harness can have,
because it reports that the tests caught a fault they never reached.

`5` exists because that is exactly what happened, in the field, against 3.5.0. A `--test-cmd` whose
path would not resolve under `cmd.exe` never started; the build succeeded, the shell returned `1`,
and the harness printed `MUTANT KILLED` and exited `0`. Detection is two-tier — the exit codes
`cmd.exe` and POSIX shells reserve for an unresolvable command, plus anchored markers in the last
three lines of the transcript — and a run that executed **zero tests**, which a `--filter` matching
nothing produces, counts as never having run.

## Usage

```powershell
# Set once per session — works whether installed via plugin or as a user skill
$mut = if ($env:CLAUDE_PLUGIN_ROOT) {
  "$env:CLAUDE_PLUGIN_ROOT\skills\mutation-harness\scripts"
} else {
  "$HOME\.claude\skills\mutation-harness\scripts"
}

dotnet run "$mut\mutate.cs" -- `
  --file src/Ordering/OrderTotalCalculator.cs `
  --find '>= threshold' --replace '> threshold' `
  --label 'discount boundary loosened' `
  --test-cmd 'dotnet test tests/Ordering.Tests -- --filter FullyQualifiedName~OrderTotal' `
  --expect-killed-by AppliesDiscountAtExactlyTheThreshold
```

```bash
mut="${CLAUDE_PLUGIN_ROOT:-$HOME/.claude}/skills/mutation-harness/scripts"
dotnet run "$mut/mutate.cs" -- --selftest      # 121-case battery; exit 0 iff all pass
```

The `--` after the script path is mandatory; `dotnet run` otherwise consumes the flags itself.

| Flag | Meaning |
|---|---|
| `--file` | source file to mutate |
| `--find` | exact text to replace; **must match exactly once**, or the run is refused |
| `--replace` | substitute; `""` is valid and deletes the anchor |
| `--label` | human description, echoed into the record. Required — an unlabelled mutant is unreadable a week later |
| `--test-cmd` | any command that exits non-zero on failure |
| `--expect-killed-by` | test-name substring that MUST fail; repeatable |
| `--timeout` | seconds, default 600 |
| `--show-output` | print exactly what the test command produced |

### Why the anchor must match exactly once

An anchor matching twice means the mutation is not the one `--label` claims, and the report would
describe a change that did not happen. The run is refused rather than guessed at.

### `--expect-killed-by` turns "something died" into "the right thing died"

Without it, any red suite counts. A test failing for an unrelated reason would read as a kill. With
it, the named test must be among the failures — the difference between a real mutation test and a
coincidence.

If no test name can be parsed from the runner's output, the harness says so and **fails** rather
than assuming. An unverifiable expectation is not a satisfied one.

## Choosing a mutant

`test-filter-development`'s `seeding-mutants.md` holds the catalogue — invert a boundary, negate a
condition, return a constant, drop a guard, swap operands, off-by-one, remove a side effect, weaken
a filter. Two rules from it that this tool enforces mechanically:

- **One mutant at a time.** Two mutants and a red suite tells you one of them was caught.
- **Mutate production code, never the test.** Changing the test to make it fail proves nothing.

**A mutant that breaks the build is not a mutant.** If the project sets `TreatWarningsAsErrors`,
dropping a call can make a variable unused and turn `IDE0060` into a build error. The harness reports
that as `BUILD-KILLED` rather than counting it, because it proves nothing about the tests. Pick a
mutation that still compiles.

## Mutation is a filter and a measure — never a target

Binary and per-mutant, or not at all. **Do not set a threshold gate.** Aggregating runs to learn
something — which of two suites is stronger, whether a module's tests are worth anything — is fine
and is a different act from gating on the aggregate. See the filter-not-target section of
`test-filter-development` for why, including that mutation score's correlation with real-fault
detection is an open question specifically for LLM-generated suites.

## Known limitations

- **It cannot mutate itself.** `dotnet run` holds a lock on the compiled assembly, so a nested run
  of the same script fails to build. The harness reports this correctly as `BUILD-KILLED`. To mutate
  `mutate.cs`, copy it elsewhere and mutate the copy.
- **Test-name extraction is per-runner.** MTP/MSTest — both the direct-binary shape and the
  `error test failed: Name (85ms):` shape `dotnet test` emits — plus VSTest, xUnit, NUnit,
  Jest/Vitest, pytest and a generic `FAIL <name>` shape. An unrecognized runner still gives a
  correct kill/survive verdict, but this is **no longer only an `--expect-killed-by` concern**:
  parsed names are what keep a genuine kill from being mistaken for a command that never ran, when
  the code under test happens to print something shell-shaped. Add a pattern to
  `Mutation.FailedTests` for a new runner.
- **`--show-output` is the diagnostic** when a verdict looks wrong. It prints the captured bytes and
  the child's exit code, which is how the reporting bugs in this script's own history were found.

## Testing

The pure core — anchor counting, single-occurrence replacement, build-error detection, test-name
extraction, and the verdict table — is covered by `--selftest`. The subprocess layer is covered by
cases that run real commands, since nothing pure reaches it.

**Every defect this script has had was found by running it against a real suite, and none by its
own battery** — the composition axis `test-filter-development` describes. A runner format the parser
did not know; a timeout scored as a kill; a never-run command scored as a kill; and, in 3.6.0, that
same never-run command again, plus `--expect-killed-by` being unusable on MSTest under MTP, plus a
killed mutant recording itself as `SURVIVED`. All of them one shape — **claiming knowledge the run
did not produce.**

The never-run case appearing twice is worth stating plainly, because this page and the 3.3.0 release
notes both claimed it fixed when it was not. What shipped then was the `cmd.exe` quoting change,
which the source itself documents as an unproven fix for an unexplained failure; the reporting was
never hardened, so the defect was still live and a field report found it. Exit `5` and
`Mutation.NeverRan` are what actually close it.

The battery is the weaker half by construction: its assertions are strings written from what a
runner was *believed* to print, never captured from one. See the backlog stub
**fixture-corpus-for-code-helper-scripts**.
