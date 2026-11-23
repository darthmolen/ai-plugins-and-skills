# The Composition Axis — What No Filter Reaches

**Load this reference when:** you are deciding whether to mock a component, or a unit-tested piece
of work has to be shown to run in the assembled system. `SKILL.md` carries the rules in short.

A perfect filter cannot expand a test's scope.

> **A mock is an unverified claim about how the system composes.**

In a mocked unit test the mock *is* the registration you forgot. You supply by hand the exact thing
production does not supply, so the test passes **because of** the defect rather than despite it.

The canonical case: a service is written, unit-tested, green on every assertion, mutation-checked,
and **never added to the bootstrap**. It is dead in production and your suite has nothing to say
about it. No amount of better unit testing reaches this. A suite at a perfect mutation score with
every assertion validated is still green.

**Why this is characteristic of AI-assisted work rather than merely common:** the agent writes the
service in one file; the composition root is a different file, frequently not in context. It emits
plausible code for the thing and never sees the assembled system.

What follows:

- Every mock is a scope reduction you are choosing. Name what it costs you before adding it.
- **For any bug that could live in more than one component, the integration test is mandatory**, and
  you may not mock the component the bug might live in. See the testing protocol in `CLAUDE.md` —
  this predates the argument, which is what makes it worth citing.
- Require at least one E2E integration test per feature via `WebApplicationFactory<TProgram>`
  (or your stack's equivalent full-composition harness) for exactly this reason.
- Where local guidance gives each session its own disposable environment for integration and
  end-to-end runs, run them only inside it, and never widen it to make a test pass. The rules are in
  [`isolated-environments.md`](isolated-environments.md).
