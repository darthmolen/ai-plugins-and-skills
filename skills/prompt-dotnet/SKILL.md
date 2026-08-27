---
name: prompt-dotnet
description: >
  Composes and executes a standardized, spec-driven, test-first .NET feature
  prompt. Invoke as /prompt-dotnet followed by the feature description. Chains
  spec-tier (stakes tiering + review gates), test-filter-development
  (RED-GREEN-REFACTOR with captured failure output and a seeded mutant), and
  csharp-quality-developer; if the harness lacks those skills, the inlined
  discipline is the contract. Oriented to ASP.NET Core services/APIs (the
  integration test uses WebApplicationFactory). Use when implementing a .NET
  feature and you want tiered spec depth, an evidenced test filter, and mandatory
  review.
metadata:
  category: workflow-composers
---

# .NET Feature Prompt (prompt-dotnet)

This skill is a **prompt composer**: it takes `args` (the feature description) and drives a spec-driven,
test-first .NET workflow by chaining `spec-tier`, `test-filter-development`, and `csharp-quality-developer`.

**How to run it:** substitute the user's `args` into `FEATURE` in the prompt below, then execute the
prompt. Invoke each of the three skills via the Skill tool as you reach it if your harness has them; if it
does not, the inlined discipline in the prompt **is** the contract — follow it exactly.

---

You are implementing a .NET feature using a spec-driven, test-first workflow. Apply these skills if your harness has them — `spec-tier`, `test-filter-development`, `csharp-quality-developer`; if not, the inlined discipline below IS the contract, follow it exactly.

FEATURE:
> {args}

STEP 1 — Tier the work (spec-tier). Score the stakes across: blast radius; cost-of-being-wrong (reversibility, security, customer/data, public API); verification cost; executor context; novelty/ambiguity. Worst factor dominates. A .NET feature ships code, so it lands at `simple` or `complex` (never the no-code/throw-away exemption). Declare the chosen tier and the review gates at the TOP of the plan file. Gate rules:
- The code-review caboose is MANDATORY on completion regardless of tier (it ships).
- If `complex`, an adversarial plan review by a DIFFERENT agent/harness is REQUIRED before any code is written.

STEP 2 — Write the plan at the tier's depth, using `test-filter-development` (RED-GREEN-REFACTOR, plus a seeded mutant after GREEN) and `csharp-quality-developer`. The plan must cover:
- Acceptance criteria (what "done" looks like from the outside)
- Data contracts / API surface (request/response shapes, routes, status codes)
- Layers to touch and their responsibilities
- Test strategy: unit tests per layer, plus at minimum one integration test
- Per cycle, the **mutant** the test must catch — the one wrong-but-plausible change to the production code (inverted boundary, returned constant, dropped guard) that the assertion has to turn red

Depth by tier:
- `simple` — design summary + locked key decisions + a RED-GREEN-REFACTOR cycle list where each cycle names its exact test seam/file, its assertions, and the mutant it must catch + an explicit Non-goals/Out-of-scope fence + acceptance criteria.
- `complex` — all of the above, PLUS: decisions grounded in `file.cs:line` ("verified against source", real signatures — not pseudocode); integration + `-warnaserror` build gates; an effort estimate; a critical-files-for-the-executor list; and a Review Response table capturing the adversarial plan-review round-trip.

Integration test requirement: add at least one integration test using `WebApplicationFactory<TEntryPoint>` (`Microsoft.AspNetCore.Mvc.Testing`) that hosts the app in-process (TestServer — no Kestrel, no port binding). It must emit AI-readable structured logs: named scopes, semantic property names, and machine-parseable log levels. Assert on structured properties and log levels, never on prose substrings.

Do NOT prescribe implementation details beyond the plan — the RED-GREEN-REFACTOR cycle during execution discovers the implementation.

STEP 3 — Execute only after the plan is approved (and, if `complex`, after the adversarial plan review clears). Per phase:
- **RED** — write the failing test, run it, and **paste the failure output** into the transcript or the commit. Confirm it fails because the behavior is missing, not because of a typo or a broken fixture. "I watched it fail" is a claim; the output is the result, and only the result counts.
- **GREEN** — minimum production code to pass. Run again, capture the pass, output pristine (no warnings).
- **MUTATE** — seed the mutant the plan named into the code under test (invert a boundary, return a constant, drop a guard). Run the covering tests with `dotnet test --filter`. The suite MUST go red. Then restore, and prove the restore with `git diff` clean on that file. **A mutant that survives means the test is rejected** — strengthen the assertion and re-run it before continuing. Judge each mutant individually; never set a mutation-score threshold as a gate.
- **REFACTOR** — clean up with the validated test as the net → next phase.

Implement the `WebApplicationFactory` integration test as its own phase; never skip or defer it — a mocked unit test cannot see a composition defect by construction, because the mock *is* the registration you forgot. All C# must satisfy `csharp-quality-developer` (StyleCop, `this.` qualification, LoggerMessage source generators, CRLF, builds clean under `-warnaserror`).

On completion, queue the change for adversarial code review on a different harness — this is the review floor and is not optional for shipped code.

---

## Notes on WebApplicationFactory

`WebApplicationFactory<TEntryPoint>` from `Microsoft.AspNetCore.Mvc.Testing`:

- Hosts the app via `TestServer` — **in-process, no Kestrel, no port binding**
- Call `factory.CreateClient()` for an `HttpClient` wired to the in-memory pipeline
- Override `ConfigureWebHost` to swap services, set test configuration, or
  capture log output via a custom `ILoggerProvider`
- Prefer `IClassFixture<WebApplicationFactory<TEntryPoint>>` in xUnit to share
  the factory across tests in a class without restarting the host

For AI-readable logs in tests, inject a `ITestOutputHelper`-backed logger or
capture via a `FakeLoggerProvider` that buffers `LogEntry` records. Assert on
`entry.Message`, `entry.LogLevel`, and structured properties — not on prose substrings.
