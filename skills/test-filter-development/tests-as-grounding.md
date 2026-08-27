# Tests as Grounding

*Why test-driven development stopped being a preference and became the mechanism. Truth about the tool, not a policy argument.*

---

## First, be honest about why TDD was a dirty word

It earned the reputation. Pretending otherwise loses the room before the argument starts.

- **It took roughly twice as long.** You wrote the feature and you wrote the tests. That was real, and on a deadline it was the thing that got cut.
- **The tests were often wrong in a way nobody noticed.** A test that passes tells you something passed. Whether it was the thing you cared about is a separate question, and plenty of suites were green while asserting nothing that mattered.
- **It broke flow.** Writing the assertion before the thing exists is genuinely uncomfortable, and the people who liked it least were often the fastest developers.

Every one of those was true. The case below is not that they were wrong — it is that **the economics they were true under no longer exist.**

## What changed

Follow the chain. Each step is uncontroversial on its own; the conclusion is where it lands.

### What is AI grounded in? — **Words.**

A model predicts tokens. Its entire world is text. That is the whole of what it is.

### What do we want it to produce? — **Code.**

Which is also words. So far there is no gap: text in, text out, and the model is genuinely good at it.

### What does code produce? — **Behavior.**

And behavior is **not words**. This is where the gap opens.

Your program's meaning is what it does at runtime — the rows it returns, the exception it throws at 3am, the screen it fails to render. None of that is text, and none of it is anything the model can perceive. Put another way: *"the only thing it perceives is whether the text it produced resembles text that describes working software."*

### The mismatch is where hallucination comes from

This is the part worth sitting with, because it reframes hallucination from *mysterious model flaw* to *predictable consequence of a representation gap*.

The model is optimising over one space (plausible text) while you are judging in another (correct behavior). It has no signal from your space. So when it reaches the edge of what the text supports, it does the only thing it can — produces the most plausible continuation and presents it with the same confidence as everything else.

It is not lying and it is not guessing badly. **It is searching in the wrong space, because the right space was never made visible to it.** No larger model fixes this. No larger context window fixes this. The gap is structural.

### What is AI extremely good at? — **Productivity.**

Volume, speed, breadth, and throwaway work that is now effectively free. Those are the real strengths.

Which means the old objection — *"TDD takes twice as long"* — is aimed at a cost that has collapsed. Writing the test is no longer the expensive half of anything.

### What do tests produce? — **Words. From behavior.**

That is the whole answer.

A test is the one artifact that exists in **both spaces at once**. It is text — so the model can read it, write it, and reason over it. And it is a statement *about runtime behavior* — so it carries information from the space the model cannot see back into the space where it works.

**A test is a transducer.** It converts behavior into words.

```text
  intent ──► code ──► behavior          the model can see the first two,
             (words)   (not words)      and is judged on the third

  behavior ──► test ──► words           the test carries it back
```

Grounding is what we want — the model working from your real material rather than a plausible reconstruction. Documentation grounds it in your *conventions*. A test grounds it in your *behavior*, which is the one thing documentation cannot do, because behavior is not a document.

That is why this is not a discipline argument. **Tests are the only channel through which a model can learn what your software actually does.**

### The bound: a transducer is worth the fidelity of its signal

Everything above stands. But notice what kind of thing it establishes — a *transducer*, and a transducer is only as good as the signal it carries. A test that asserts nothing converts nothing. The chain is sound, and the practice built on it pays out **in proportion to how good the filter on test quality is.**

> **The argument is correct, but only insofar as we can improve the filter.**

That sentence is the whole reason the practice here is specified as a *filter* rather than as an ordering. It is not a hedge on the thesis; it is the thesis stated with its coefficient. A suite of hollow tests is a transducer wired to nothing — and it is worse than no suite, because it manufactures confidence in both directions at once: yours, and the model's.

Two things follow, and they are the difference between believing this page and acting on it:

- **The ordering is not the active ingredient.** Writing the test first protects the *independence* of the test from the implementation, which is real and worth keeping. It is a supporting role. The filter — the mechanical proof that this test fails when the behavior is wrong — is what carries the value.
- **The filter has to be mechanical, because judgement is the thing that is failing.** If you could reliably tell a good assertion from a hollow one by reading it, the AI could too. You cannot always, and neither can it. So the check has to be something you *run*.

See [the evidence gap brief](tdd-agent-evidence-gap.md) for what the literature does and does not support here. The short version, because it is load-bearing for this page: the *instruction* to write tests first moves agent outcomes by at most ~2.6pp, while a *verifiable filter* has production evidence behind it. Those are two different mechanisms, and only the second one is doing work.

## What this changes about the old objections

| Objection | Then | Now |
|---|---|---|
| "Twice as long" | True — you wrote both halves | Production is cheap; **verification is the bottleneck**. TDD moves effort to where the constraint actually is. |
| "Tests that assert nothing" | A real and common failure | **Worse, and more likely.** See below. |
| "It kills flow" | Fair | The AI writes the test. Your job moved to deciding whether the assertion is the right one. |

## The RED step is now load-bearing, not ceremony

This is the one place the old criticism got *sharper* rather than weaker, and it is the most important practical point on this page.

An AI will happily produce a test that looks thorough, reads well, and asserts nothing meaningful. It is optimising for text that resembles a good test — which is exactly the failure mode described above, aimed at your test suite instead of your code. A green suite full of such tests is worse than no suite, because it manufactures confidence.

**A test that has never failed is not evidence.** RED → GREEN → REFACTOR survives because the RED step is a mechanical proof that the test can detect *something*. Watch it fail for the right reason, then make it pass.

Skipping RED with an AI in the loop is not a shortcut. It removes the cheapest guard against the failure mode the AI is most prone to.

Two corollaries worth stating:

- **A test the agent never ran is a claim, not a result.** "I've verified this works" from an agent that executed nothing is the sound of nothing having been verified.
- **An agent that can run your tests can correct itself before you ever see the work.** The quality difference between an agent with tools and one producing prose is not subtle — and tests are the highest-value tool you can give it, because they are the one that reports on behavior.

### What RED proves, and the exact thing it does not

Be precise here, because "we do RED" is where teams stop and it is not far enough.

RED is a filter with **one mutant in it**, and that mutant is *the implementation does not exist yet*. So RED proves the test can detect **absent**. It does not prove the test can detect **present-and-wrong** — a function that exists, runs, returns a plausible value, and computes the wrong thing. That is a strictly harder fault to catch, it is the fault you will actually ship, and a test can pass RED while being blind to every instance of it.

The fix is cheap and mechanical, which is why it is now required rather than recommended:

> **After the test is green, break the production code on purpose — invert a boundary, return a constant, drop a guard clause — and confirm the suite goes red. Then restore it.**
>
> A suite that stays green through that is rejected, not noted. It has just demonstrated, in front of you, that the behavior can break while your tests pass.

Seconds to run, deterministic, and it catches the whole class of defect RED structurally cannot see. One caveat that matters: treat this as a **filter** — does this test kill this specific seeded fault, yes or no — and never as a percentage to optimise toward. A score target invites tests written to move the score, which is the hollow-test failure mode with a metric bolted on.

Two axes sit alongside this one and are worth keeping distinct, because they impose *different* bars:

- **Regression.** A test also earns value later, when someone changes the code. That job has a **low** bar — a mediocre test that merely pins current output is a serviceable tripwire, and it does something better than detect: it **names** what moved. This is the best-evidenced claim of the three: hold the agent fixed, add only the regression set, and resolution rates rise while cost falls.
- **Composition.** Integration tests buy **scope**, not quality, and no improvement to a unit test reaches it. A mock is an unverified claim about how the system composes — in a mocked unit test the mock *is* the registration you forgot, so the test passes *because of* the defect. A service written, unit-tested, mutation-checked and never added to the bootstrap is dead in production with a perfectly green suite. This is characteristic of AI-assisted work rather than merely common: the agent writes the service in one file and the composition root is a different file, frequently not in context. It is the words/behavior gap, moved up a level from the unit to the assembled system.

## Where this lands

This standard requires that AI-assisted application code be covered by tests **proven able to fail for the right reason** — captured RED output, and a seeded mutant after GREEN. See [`test-filter-development`](SKILL.md) for the cycle. This page is why, and it is worth knowing the why rather than the rule, because a rule whose reason you do not believe gets skipped under deadline.

Note what the mandate is *not*: it is not the ordering. Writing the test first is kept because it protects the independence of the test from the implementation, and it is cheap. It is not what the requirement is about, because it is not what the evidence is about.

**The standards document got here first**, and independently. Its rationale for TDD already says *"AI produces code that looks plausible but may be subtly wrong… Test output is text (logs, assertion messages, stack traces) that AI can read and reason about. Without this feedback loop, AI hallucinates correctness."* Its rationale for end-to-end tests adds that real logs are *"text that grounds the AI's understanding of what is actually happening."*

That is the same conclusion, reached from the practice rather than from the mechanism. Worth being precise about what that is and is not worth.

### What we actually have, and what we are missing

Be honest about the shape of the case, because this page spends its length telling you to distrust confident claims:

| | What it is | What it establishes |
|---|---|---|
| **The mechanism** | The words/behavior gap above. **Not original to this page** — it is general, broadly accepted reasoning about how these models work. | A plausible causal story for *why* tests would help. |
| **The observation** | In-house standards reached the same practice from experience. | That practitioners hit the problem and landed on the same fix. |
| **The empirical evidence** | **Missing for the claim on this page.** Measured outcomes — defect rates, escaped bugs, rework — with and without the discipline. Partial elsewhere: see below. | Whether it actually works, and by how much. |

**Where the empirical picture is not blank, and why that does not rescue this page.** The literature does measure some adjacent things, and the split is sharp enough to be useful. A *verifiable filter* on test quality has production evidence — mutation-guided hardening deployed at scale, and a 50% drop in fault detection when the mutation loop is removed. *Existing regression tests* improve agent outcomes in the cleanest ablation available, agent held fixed. But the *instruction* to write tests first moves outcomes by at most ~2.6pp, and **nobody has run the experiment this page describes**: deterministic production code, an agent implementing under a test-first guardrail versus test-after, escaped defects measured downstream. The brief is [`tdd-agent-evidence-gap.md`](tdd-agent-evidence-gap.md).

So the honest position is narrower and more useful than "no evidence": the leg under the *filter* has support, the leg under the *ordering* does not, and the grounding argument on this page is still carried by mechanism and observation alone.

A mechanism and an observation pointing the same way is a **real signal and a weak one**. Both are *a priori*: one is a causal argument, the other is one organisation's experience. Neither measures an outcome. Convergence between them is worth something — it would be worse if the practice contradicted the mechanism — but two theoretical routes do not add up to an empirical one.

**Mechanism + observation + measured outcomes is a strong signal. We have two of the three.**

The third is gettable, which is why its absence is a gap rather than an excuse. Either the published literature has it, or an organisation can produce it from its own delivery data. Until somebody does that work, the case for TDD here rests on a good argument and lived experience — which is enough to adopt a practice, and not enough to call the question closed.

The short version:

> **AI is grounded in words. Code produces behavior. Tests turn behavior back into words.**
>
> That is the only bridge across the gap, and everything else is commentary.
