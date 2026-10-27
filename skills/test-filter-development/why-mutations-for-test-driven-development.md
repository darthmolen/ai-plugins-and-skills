# Why We Break Code On Purpose

*The MUTATE step, in plain language: what it is, why it exists, and what the rest of the world
calls it. For whether the evidence supports gating on it, see
[the evidence gap](tdd-agent-evidence-gap.md) — this page is the concept, that one is the case.*

## The MUTATE step in one sentence

**We break the code on purpose to see whether the tests complain.**

If they do, the tests are doing their job. If they stay green while the code is provably wrong,
they were never testing what you thought.

## The thing a green suite does not tell you

A passing test proves the code ran. It does not prove the test would notice if the code were
wrong. Those are different claims, and only the first one is checked by running the suite.

This is easy to see once stated and almost impossible to see in a diff:

```csharp
[TestMethod]
public void AppliesDiscount()
{
    var total = Calculator.Total(new Order { Subtotal = 100m, IsMember = true });
    Assert.IsNotNull(total);        // green. forever. under every possible bug.
}
```

100% line coverage. Executes every branch you care about. Catches nothing. Change `>=` to `>`,
return a constant, delete the discount entirely — still green.

Coverage tells you which lines *ran*. It says nothing about whether anything would have
objected. So the only way to find out is to introduce a fault and watch:

```
- if (subtotal >= threshold)
+ if (subtotal > threshold)
```

Run the tests. A test that fails has just proved it was load-bearing. A suite that stays green has
just proved it was decoration.

## Why this bites harder with an agent

An agent writes tests that pass. That is the instruction it was given, and it is very good at it.
Nothing in "write tests for this" distinguishes *a test that would catch a regression* from *a
test shaped like one*, and the second is cheaper to produce and looks identical in review.

Human-written suites have the same failure mode; the difference is volume and the reviewer's
attention budget. A person writing forty assertions by hand feels the fifth one being pointless. A
model does not, and neither does the person reading forty assertions that all look reasonable.

Mutation is the check that does not care how the test was written or who wrote it. It asks the
only question that settles it: *does this test fail when the code is wrong?*

## Mutation testing: origins and assumptions

The local tooling is homegrown, which can give the wrong impression; we did not invent the
technique. It is called **mutation testing** and it is about as old as software testing research.

| | |
|---|---|
| **1971** | Richard Lipton, *Fault Diagnosis of Computer Programs* — a Carnegie Mellon student report, where the idea first appears |
| **1978** | DeMillo, Lipton & Sayward, *Hints on Test Data Selection: Help for the Practicing Programmer*, IEEE Computer — the paper that formalized it |
| **1980** | Timothy Budd builds the first working tool |

The 1978 paper also named the two assumptions the technique rests on, which tell you when it works
and when it does not:

- **The competent programmer hypothesis.** Programmers write nearly-correct code, so real defects
  tend to be small deviations — not arbitrary nonsense. That is why a *single small* seeded fault
  is representative of a real one.
- **The coupling effect.** A test suite that catches simple faults tends also to catch complex
  ones. That is why you do not need to seed realistic multi-part bugs to learn something.

Both are empirical claims, not theorems. They have held up well enough for the technique to be in
production use for decades, but they are the reason the answer is *evidence*, not *proof*.

### Mutation testing vocabulary

| Term | Meaning |
|---|---|
| **mutant** | one deliberately introduced fault |
| **mutation operator** | the class of change — invert a boundary, negate a condition, return a constant. Our catalogue is `seeding-mutants.md` |
| **killed** | at least one test failed. Good |
| **survived** | the suite stayed green with the fault in place. **This is the finding** |
| **mutation score** | killed ÷ total non-equivalent mutants |
| **equivalent mutant** | a mutant that changes the code but not its behavior, so nothing can ever kill it. Detecting these in general is **undecidable** — it is the field's classic hard problem, and the reason a mutation score is never cleanly 100% |

### Mutation testing tools

| Tool | Ecosystem |
|---|---|
| **PIT / PITest** | Java and JVM — mutates bytecode, which is what made this practical at scale |
| **Stryker** (incl. **Stryker.NET**) | JavaScript, TypeScript, C#, Scala |
| **mutmut**, **Cosmic Ray** | Python |
| **Infection** | PHP |
| **cargo-mutants** | Rust |

Modern implementations run only the tests covering each mutant, parallelise, and mutate only
changed code, which is how organisations run this continuously rather than overnight.

### Why this repository ships its own script alongside Stryker.NET

**Stryker.NET** exists and is better at what it does, but the two do different jobs. Stryker
answers *how strong is this suite?* — it generates hundreds of mutants across a project and
reports a score. `mutation-harness` answers *is this one
test, which I just wrote, load-bearing?* — one named mutant, one command, one verdict, and the
file restored byte-for-byte whatever happens. It is a step inside the red-green loop, not a report
you read afterwards, and it is deliberately framework-agnostic via `--test-cmd` so it works the
same for .NET, Node and Python.

Use Stryker.NET when you want to know where a codebase's testing is weak. Use `mutation-harness`
when you want to know whether the test you are writing right now would have caught the bug you are
fixing.

## Gating on mutation score

**Mutation score is a filter, never a target.**

Per-mutant and binary — *did this test kill this fault, yes or no* — is a question with a useful
answer. A percentage gate on a whole suite is Goodhart's Law with extra steps: the cheapest way to
raise the number is to write assertions that kill mutants, which is not the same as writing tests
that describe behavior, and a team optimising the number will find that out.

Aggregating scores to *learn* something — which of two suites is stronger, whether a module's
tests are worth anything — is fine, and is a different act from gating on the aggregate. The
distinction, with the studies on how well mutation score correlates with real-fault
detection, is in [the evidence gap](tdd-agent-evidence-gap.md), including the open question of
whether that correlation holds for LLM-generated suites specifically.

## What mutation cannot tell you

Mutation proves a test is **sensitive**. It cannot prove the test is **about anything**, and it
cannot prove the test is **right**. Three failure modes, and it only closes one:

| Failure | Symptom | Caught by mutation? |
|---|---|---|
| The test is too **weak** | survives the mutant | **yes** |
| The test is too **coupled** | kills the mutant *and* breaks on any refactor | **no** — it rewards this |
| The test asserts the **wrong answer** | kills mutants, confidently wrong | **no** — it cannot see it |

### It rewards coupling

The most mutation-sensitive test you can write is one that asserts on implementation:

```csharp
mockLogger.Verify(x => x.Log("discount applied"), Times.Once);
```

A perfect mutant killer. Delete the log line and it goes red. It tests nothing anyone is paying
for, and it blocks refactoring. These are **change-detector tests**, and a high mutation
score is exactly what a suite full of them produces. Sensitivity is necessary and nowhere near
sufficient.

### It cannot check the expected value — the oracle problem

This is the one to worry about, because it is where the tooling gives *false* confidence rather
than none.

Assert `Total == 90` when the rule says 85, and mutation is perfectly satisfied: the test is
sensitive, deterministic, repeatable, and **wrong**. Nothing in the technique looks at whether 90
was ever the right answer. This is the **test oracle problem**, and no amount of mutation touches
it.

It is the sharpest hazard in agent-written tests specifically, because the cheapest way for a model
to produce a passing test is to run the code and assert whatever came out. That is a flawless
regression detector for the bug you currently have. It kills mutants beautifully.

**The only answers derive the expected value from somewhere other than the code**: the spec, a
worked example someone calculated by hand, a second independent implementation, or a property that
must hold regardless (`total <= subtotal`, `discount >= 0`).

### And the form it usually takes: nobody wrote down what must *not* change

Writing a precise spec closes most of the oracle gap. It does not close this part of it.

Asked to implement *delete a user's PII*, an implementation of `TRUNCATE TABLE Users` will satisfy
a test asserting *the user's PII is gone*. The assertion is true. The spec is met. Mutants die on
it. Every gate in this document is green, and the feature destroys the table.

The assertion named a **necessary** condition and treated it as **sufficient**. What was never
written down is what had to survive: the other users' rows, the non-PII columns, the foreign keys.
This is the **frame problem** — in program verification, the *frame condition*, the statement of
what an operation does **not** modify. Specs state postconditions almost by reflex and frame
conditions almost never, and a spec that is highly specific about the change can still say nothing
about the blast radius.

The practical rule, and it costs one assertion:

> For any destructive or mutating operation, assert **what survived**, not only what changed.

And the mutant that finds out whether your suite pins it: widen the scope of a destructive
operation — drop the `WHERE`, remove the tenant filter, delete the `userId` predicate. If no test
dies, your suite does not constrain the blast radius, and it never did.

## What the combination is called

TDD with a mutation gate is published as **TDD+M**, in Roman & Mnich, *Test-driven development
with mutation testing – an experimental study* (Software Quality Journal, 2021) — a controlled
experiment, not a coinage. It split participants between plain TDD and a mutation-augmented
variant, and the mutation groups came out ahead on both axes: **64.9% mutation coverage against
37.5%**, and **53.3% statement coverage against 33.5%**.

That is the closest thing to a market name for what the `test-filter-development` skill describes.
We write it **TDD-M**, with a hyphen, for a reason that is duller and more durable than taste: a
term that turns up in skill names, filenames and command lines gets typed constantly, and `+`
needs shift and an awkward reach. It kept coming out as `-` anyway. Search for `TDD+M`; that spelling finds
the paper.

### The house name: test-filter-development

`test-filter-development` is a house term, and "filter" in it means *a test that rejects bad code
is a filter; one that passes everything is not.* That sense is deliberate and is used the same way
throughout these docs.

It is also the weakest word in the vocabulary, for a reason specific to this repository: `--filter`
already means *the expression selecting which tests to run* in the very commands the skill tells
you to type, and 3.6.0 added a verdict for a `--filter` that matches nothing. A rename to **`tdd-m`**
— short enough to type, with *mutation-driven development* carried in the description — is recorded
as **rename-test-filter-development-to-tdd-m**.

## Applying mutation when writing tests

- Writing a test? After it goes green, break the code it covers and confirm it goes red.
- Use `mutation-harness` rather than doing it by hand. A mutation run interrupted between seed and
  revert leaves a deliberate bug in your working tree, and the next commit ships it.
- A surviving mutant **rejects the test**. It is not a note to self. Strengthen the assertion and
  run it again.
- One mutant at a time. Two mutants and a red suite tells you one of them was caught.
- Mutate production code, never the test. Changing the test to make it fail proves nothing.

## Sources

- R. Lipton, *Fault Diagnosis of Computer Programs*, student report, Carnegie Mellon University, 1971
- R. DeMillo, R. Lipton, F. Sayward, *Hints on Test Data Selection: Help for the Practicing
  Programmer*, IEEE Computer 11(4), April 1978 — the coupling effect and the competent programmer
  hypothesis
- T. Budd, first mutation testing implementation, 1980
- A. Roman, M. Mnich, *Test-driven development with mutation testing – an experimental study*,
  Software Quality Journal, 2021 — <https://doi.org/10.1007/s11219-020-09534-x>
- Y. Jia, M. Harman, *An Analysis and Survey of the Development of Mutation Testing*, CREST, King's
  College London, TR-09-06 — the standard survey of the field
- `theofidry/awesome-mutation-testing` — maintained index of tooling per ecosystem
- For the correlation evidence and its limits for LLM-generated suites, see
  [the evidence gap](tdd-agent-evidence-gap.md), which carries its own sources
