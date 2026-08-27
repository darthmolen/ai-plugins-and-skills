# Testing Anti-Patterns

**Load this reference when:** writing or changing tests, adding a mock, or tempted to add a test-only
method to production code.

## The mechanism, not the prescription

The usual rule is *"no mocks unless unavoidable."* That is a prescription with its reason left out,
so it gets negotiated away the first time a mock is convenient. Here is the reason:

> **A mock is an unverified claim about how the system composes.**

When you mock a collaborator you are asserting, by hand, that production will supply that
collaborator in that shape. Nothing checks the assertion. And in the failure case that matters most,
**the mock *is* the wiring you forgot** — you supply the missing registration yourself, inside the
test, so the test passes *because of* the defect rather than despite it.

That is why a mocked unit test cannot see a composition defect **by construction**. Not "usually
misses"; cannot see. No improvement to the assertion reaches it, because the assertion is not the
problem — the scope is.

**Why this is characteristic of AI-assisted work.** The agent writes the service in one file. The
composition root is a different file, frequently not in context. It emits plausible code for the
thing and never sees the assembled system — the words/behavior gap, moved up one level from the unit
to the composition.

**Core principle:** test what the code does, not what the mocks do. Every mock is a scope reduction
you are choosing; name what it costs before you add it.

## The Iron Laws

```
1. NEVER test mock behavior
2. NEVER add test-only methods to production classes
3. NEVER mock without understanding the dependency chain
4. NEVER mock the component a bug might live in
```

Law 4 is a protocol, not a preference — pin it in your project's `CLAUDE.md`. For a
bug that could live in more than one component, the integration test against the real component is
mandatory.

---

## Anti-Pattern 1: Testing Mock Behavior

```typescript
// ❌ Verifying that the mock exists
test('renders sidebar', () => {
  render(<Page />);
  expect(screen.getByTestId('sidebar-mock')).toBeInTheDocument();
});
```

**Why it is wrong:** it passes when the mock is present and fails when it is not. It reports on your
test setup. Nothing about the real component can make it fail — which means it is hollow by the
mutant test: no change to `Sidebar` turns it red.

```typescript
// ✅ Test the real component, or do not assert on the mock at all
test('renders sidebar', () => {
  render(<Page />);            // don't mock the sidebar
  expect(screen.getByRole('navigation')).toBeInTheDocument();
});
```

**Gate:**

```
BEFORE asserting on any mock element:
  Ask: "What change to production code would make this assertion fail?"
  IF the answer is "none" or "changing the mock":
    STOP - delete the assertion, or unmock the component
```

## Anti-Pattern 2: Test-Only Methods in Production

```csharp
// ❌ Reset() exists only so tests can start clean
public class OrderCache
{
    public void Reset() => this.entries.Clear();   // looks like production API
}
```

**Why it is wrong:** the production class is polluted with test-only surface, it is dangerous if
called for real, and it confuses object lifecycle with entity lifecycle.

**The fix:** put it in test utilities. Construct a fresh instance per test, or expose a factory the
tests use.

**Gate:**

```
BEFORE adding any method to a production class:
  Ask: "Is this only used by tests?"           IF yes -> test utilities instead
  Ask: "Does this class own that lifecycle?"   IF no  -> wrong class
```

## Anti-Pattern 3: Mocking Without Understanding

```typescript
// ❌ The mock removes the side effect the test depends on
test('detects duplicate server', async () => {
  vi.mock('ToolCatalog', () => ({ discoverAndCacheTools: vi.fn() }));  // also skipped the config write

  await addServer(config);
  await addServer(config);   // should throw - but won't
});
```

**Why it is wrong:** you mocked at a level that owns behavior the test needs, so the test passes or
fails for reasons unrelated to what it claims to check.

**Gate:**

```
BEFORE mocking any method:
  1. What side effects does the real method have?
  2. Does this test depend on any of them?
  3. Do I understand what this test actually needs?

  IF it depends on side effects:
    Mock lower - the slow or external operation - not the method the test depends on
  IF unsure:
    Run the test against the real implementation FIRST, observe what it needs,
    THEN mock minimally at the right level

  Red flags: "I'll mock this to be safe" - "this might be slow, better mock it"
```

## Anti-Pattern 4: Incomplete Mocks

```typescript
// ❌ Only the fields you happened to think of
const mockResponse = { status: 'success', data: { userId: '123', name: 'Alice' } };
// breaks later when code reads response.metadata.requestId
```

**Why it is wrong:** a partial mock hides a structural assumption. Downstream code depends on fields
you omitted, so the test passes while the integration fails — the composition defect again, in
miniature.

**Iron rule:** mock the **complete** structure as it exists in reality, not the subset your immediate
assertion touches.

```typescript
// ✅ Mirror the real response
const mockResponse = {
  status: 'success',
  data: { userId: '123', name: 'Alice' },
  metadata: { requestId: 'req-789', timestamp: 1234567890 },
};
```

## Anti-Pattern 5: The Service That Was Never Registered

The composition failure in its canonical form, and the reason this file leads with the mechanism.

```csharp
// ✅ Written. ✅ Unit-tested. ✅ Every assertion validated. ✅ Mutants killed.
public sealed class DiscountCalculator : IDiscountCalculator { /* ... */ }

// ❌ Program.cs — never added
// builder.Services.AddScoped<IDiscountCalculator, DiscountCalculator>();
```

Every unit test passes, because every unit test constructed the calculator itself. Production
resolves `IDiscountCalculator`, finds nothing, and the feature is dead. **A suite at a perfect
mutation score is still green here.**

**The only thing that catches it** is a test that composes the system the way production does:
`WebApplicationFactory<TProgram>` hitting the real endpoint through the real container. Require
one per feature for exactly this reason.

**Gate:**

```
BEFORE calling a feature complete:
  Ask: "Does any test resolve this through the real composition root?"
  IF no:
    The feature is unverified regardless of unit-test results
```

## Anti-Pattern 6: Tests as an Afterthought

```
✅ Implementation complete
❌ No tests
"Ready for testing"
```

Testing is part of implementation. "Ready for testing" means "not done", and a test written after the
code is conditioned on the code — you will test what you built rather than what was required.

---

## When Mocks Become Too Complex

**Warning signs:** the mock setup is longer than the test logic; you are mocking everything to make
it pass; the mock is missing methods the real component has; the test breaks when the mock changes
but not when the code does.

**The question to ask:** *do we need to be using a mock here?* An integration test with real
components is frequently simpler than the mock scaffolding it replaces — and it buys you a scope
no mock can.

## Quick Reference

| Anti-pattern | Fix |
|---|---|
| Assert on mock elements | Test the real component, or unmock it |
| Test-only methods in production | Move to test utilities |
| Mock without understanding | Understand the dependency chain, mock minimally, mock low |
| Incomplete mocks | Mirror the real structure completely |
| Service never registered | An integration test through the real composition root |
| Tests as afterthought | Test first, and capture the failure |
| Over-complex mocks | Consider an integration test instead |

## Red Flags

- An assertion that checks for a `*-mock` test ID
- A method only called from test files
- Mock setup is more than half the test
- The test fails when you *remove* the mock
- You cannot explain why the mock is needed
- Mocking "just to be safe"
- **You cannot name a change to production code that would fail this test**

## The Bottom Line

**Mocks are tools to isolate, not things to test — and every one of them is a claim about
composition that nothing is checking.**

Two questions settle almost every case in this file:

1. *What change to the production code would make this test fail?*
2. *Would this test still pass if the thing were never wired up?*

If the answer to the first is "none", the test is hollow. If the answer to the second is "yes", the
test is out of scope for the defect you are most likely to ship.

Back to the cycle: [`SKILL.md`](SKILL.md). Mutant recipes:
[`seeding-mutants.md`](seeding-mutants.md).
