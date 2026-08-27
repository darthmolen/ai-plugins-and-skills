# Seeding Mutants

**Load this reference when:** you have reached the MUTATE step and need a mutant to seed, or you are
deciding whether a mutation tool is worth wiring up.

## What a mutant is for

One deliberate, wrong-but-plausible change to the code under test, made to answer a single question:

> **Does the suite go red?**

Yes → the test detects that class of fault. No → the test is rejected, strengthened, and re-run
against the same mutant.

This is not a coverage exercise and it is not a score. One mutant, one answer, then restore.

## The rules

1. **One mutant at a time.** Two mutants and a red suite tells you one of them was caught.
2. **Mutate the production code, never the test.** Changing the test to make it fail proves nothing —
   the point is that the *behavior* moved and the test noticed.
3. **Pick a mutant that a real defect would resemble.** `return null` from everything is not a fault
   any developer would ship; an inverted boundary or a dropped guard clause is.
4. **Run only the tests that cover the mutated code.** Fast enough that this step costs seconds.
5. **Restore, and prove you restored.** See the checklist below. A seeded mutant committed by
   accident is a shipped bug you wrote on purpose.

## Mutant catalogue

Ordered roughly by how often the corresponding real defect actually occurs.

| Mutant | Example | Catches a test that... |
|---|---|---|
| **Invert a boundary** | `<` → `<=`, `>` → `>=` | only exercises values far from the boundary |
| **Negate a condition** | `if (x)` → `if (!x)` | never covers both branches |
| **Return a constant** | `return total;` → `return 0;` | asserts the shape of the result, not its value |
| **Drop a guard clause** | delete `if (input is null) throw` | never passes invalid input |
| **Swap operands** | `a - b` → `b - a` | uses inputs where the operation is symmetric |
| **Off-by-one** | `i < n` → `i < n - 1` | only checks the first element, or `.Any()` |
| **Remove a side effect** | comment out the `save()` / `Publish()` call | asserts the return value and never the effect |
| **Change a unit or a sign** | seconds → milliseconds, `+` → `-` | asserts "not null" or "greater than zero" |
| **Weaken a filter** | drop a `.Where(...)` clause | asserts the count only, or nothing about membership |

**Choosing:** pick the mutant that most resembles the defect you would actually be afraid of in this
code. If nothing comes to mind, invert the boundary — it is the highest-yield default.

---

## Per-language recipes

### C# / .NET

```powershell
# 1. Seed: edit one line in the class under test
# 2. Run only the covering tests
dotnet test --filter "FullyQualifiedName~OrderTotalCalculatorTests"
# 3. Expect: Failed! - the assertion that named the behavior
# 4. Restore
git checkout -- src/Ordering/OrderTotalCalculator.cs
git diff --stat            # must be empty for that file
```

Watch for: a mutant that breaks *compilation* is not a mutant, it is a typo — the suite must go red
on an assertion, not on a build error. If the project has `<TreatWarningsAsErrors>true`, a commented
-out call may fail the build on an unused variable; assign to `_` or pick a different mutant.

**Tooling:** [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/) generates candidate mutants
and reports survivors. Use it to *find* mutants worth judging. Do **not** configure a
`thresholds.break` score gate — see the filter-not-score rule in [`SKILL.md`](SKILL.md).

### TypeScript / Angular

```bash
npx jest src/pricing/discount.spec.ts        # or: npx vitest run src/pricing
```

Watch for: strict null checks make some mutants a compile error rather than a behavior change. Prefer
value and boundary mutants (`>= ` → `>`, a changed constant) over type-shape mutants.

For Angular, mutate the **service or pure function**, not the template. A template mutant usually
proves only that the component test renders, which is the composition question rather than the
filter question.

**Tooling:** StrykerJS, same caveat — mutants as candidates, no score gate.

### PowerShell

```powershell
Invoke-Pester -Path .\tests\Get-DriftReport.Tests.ps1
```

Watch for: PowerShell's loose comparison semantics mean `-eq` mutants often still behave. Prefer
dropping a `Where-Object` clause, changing a comparison operator (`-lt` → `-le`), or removing a
`throw` from a validation branch.

No mutation tooling in common use. Manual, from the catalogue above.

### SQL and stored procedures

Mutate the predicate, not the projection: change a `JOIN` to a `LEFT JOIN`, drop a `WHERE` clause,
change `>=` to `>` on a date boundary. A test asserting only row *count* survives most of these,
which is precisely the finding you want.

---

## Restore checklist

Run this before you claim the step is done — it is two commands and it prevents the one failure mode
that turns this practice into an outage.

```bash
git diff                # the mutated file must not appear
git status              # no unexpected modifications
```

Then re-run the suite once more and confirm green. A suite that is still red after "restoring" means
the restore was incomplete, or the mutant found a *second* real defect on its way past — either way,
stop and look.

## What to record

The MUTATE step is only evidence if someone can read it later. In the PR body, the commit, or the
transcript:

```
MUTATE: OrderTotalCalculator.ApplyDiscount — inverted the threshold boundary (>= to >)
        dotnet test --filter FullyQualifiedName~OrderTotalCalculatorTests
        Failed! - Assert.AreEqual failed. Expected:<90.00>. Actual:<100.00>.
        Restored; git diff clean.
```

Three lines. The mutant, the red run it caused, and the restore. That is the whole artifact, and it
is the difference between a filter and a checkbox.
