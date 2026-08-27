# Evidence Gap: Guardrailed Test-First Development with AI Coding Agents

**Purpose:** context brief for a Claude Code session. States what the empirical
literature does and does not establish about test-first workflows when an AI agent
writes the implementation, and lists the primary sources with identifiers.

**Scope note:** the question is narrowly about *deterministic* code under
*guardrailed* (verifiably filtered) test-first workflows. Studies of unconstrained
agent self-testing are treated as evidence about the failure mode, not as evidence
against the practice.

---

## 1. The gap

Nobody has run the experiment. The four closest bodies of work each miss it in a
different direction:

| Study | What it measures | Why it isn't the experiment |
|---|---|---|
| Meta ACH | Mutation-guided test generation on deployed production code | Hardens *existing* human-written code against seeded faults. The agent does not write the implementation. |
| TDFlow | Agentic patch generation against supplied tests | The supplied tests are the grading criteria. Circular. |
| TestPrune | Adding minimized regression tests to an existing agent loop | Reuses pre-existing human suites; no test-first ordering. |
| Currante (registered report) | Human-in-the-loop test-first workflow | LiveCodeBench puzzles, external tools disabled. Authors concede this "may under-approximate real workflows." |

**The unrun experiment:** take deterministic production code; have an agent
implement it under a test-first guardrail versus test-after; measure escaped
defects downstream. No published study does this.

**Implication for harness design:** any claim that a test-first *instruction*
improves agent output is currently unsupported. The supported claim is narrower —
that a *verifiable filter* (buildable, non-flaky, provably kills a seeded fault)
improves the quality of accepted tests. Instruction and filter are different
mechanisms, and only the second has evidence behind it.

## 2. Caveat on mutation score as the gate

Do not standardize on mutation score as the guardrail metric without reading
Chowdhury-style replication work first. A replicability study posted late July 2026
(arXiv 2607.22880) re-runs the Papadakis mutation-vs-real-fault correlation analysis
specifically for LLM-generated suites on Defects4J, asking whether mutation score is
actually predictive of real-bug detection in that population.

Mutation score is clearly better than line coverage — the ACH result that 277 of 571
fault-catching tests would have been discarded under a coverage criterion establishes
that. But "better than coverage" is not "validated as a stopping rule." The gate
metric has its own open validity question.

**Practical stance:** treat mutation score as a *filter* (does this test kill a
specific seeded fault, yes/no) rather than as a *score to optimize toward*. The
former is what ACH does and what has production evidence; the latter is what the
replication study puts in question.

---

## 3. What changed on the strength of this brief — and what is still open

**Recorded here deliberately.** A brief that changes practice should say so in the same place it
states its limits, so nobody later mistakes the change for the evidence.

**Changed, 2026-08-17.** The mandate in
The originating in-house standard previously required
Test-Driven Development — an *ordering*, attested to by whoever wrote the code. It now requires a
test **proven able to fail for the right reason**, with two mechanical checks:

1. **RED is evidenced, not attested.** The failure output is captured. A checkbox saying a test was
   run is exactly the artifact this practice exists to distrust, aimed at the enforcement mechanism
   instead of at the code.
2. **A second mutant, after GREEN.** One seeded fault in the code under test; the suite must go red;
   restore. RED alone proves a test detects *absent*, never *present-and-wrong* — one mutant, and the
   easiest one. Survival is rejection, not a note.

The skill that carries it is `csat-standards:test-filter-development`, named for the mechanism rather
than the ordering. `superpowers:test-driven-development` is retained, unmodified, so the difference
stays legible.

**Why this specific change follows from this brief.** §1 splits *instruction* from *filter* and finds
support for only the second. The old mandate was the instruction — the ~2.6pp axis. Mutation is
framed as a per-mutant filter and never as a score, which is §2's practical stance carried through
without dilution.

**What this does not close.** Everything in §1 still stands:

- **The unrun experiment is still unrun.** Nothing here measures escaped defects on deterministic
  production code under a test-first guardrail versus test-after. Hardening the filter is not the
  same as producing the missing empirical leg, and it must not be reported as though it were.
- **The gate metric still has an open validity question** (§2, arXiv:2607.22880). Binary per-mutant
  use is the narrow claim the evidence supports; a percentage threshold is not.
- **The cost question is untested here.** TestPrune found cost *fell*, but that was a regression-set
  intervention, not a mutation loop. Every added filter step costs a run. Measure it rather than
  assuming the transfer.
- **The composition claim has no isolated study.** That integration tests catch what unit tests
  cannot rests on mechanism plus pre-existing field practice — two legs, the same standing as
  the grounding argument, and it is labelled that way in the teaching material.

**If the empirical leg gets built,** the cheapest route is your own delivery data: mutant-survival
rate at first submission, and escaped defects per feature before and after this change. Neither is
instrumented today.

---

## 4. Sources

### Guardrailed generation with production evidence

- **Mutation-Guided LLM-based Test Generation at Meta (ACH)** — Foster et al., FSE
  2025. arXiv:2501.12862.
  10,795 Android Kotlin classes across 7 deployed platforms; 9,095 mutants; 571
  hardening tests. 277 of 571 would have been discarded under a line-coverage
  criterion; 51% also raised coverage. Engineer acceptance 73% in Messenger/WhatsApp
  test-a-thons, 36% judged privacy-relevant.

- **Harden and Catch for Just-in-Time Assured LLM-Based Software Testing** —
  arXiv:2504.16472.
  States the six ACH assurances (buildable; valid non-flaky regression tests; etc.)
  and the argument that guaranteeing mutant killability sidesteps the
  equivalent-mutant problem.

- **Automated Unit Test Improvement using LLMs at Meta (TestGen-LLM)** — Alshahwan
  et al., FSE 2024. Predecessor system; the assurance-filter lineage.

### The GIGO failure, quantified

- **Mutation-Guided Unit Test Generation with a Large Language Model (MutGen)** —
  Wang, Xu, Briand, Liu. arXiv:2506.02954.
  Reports suites at 100% coverage / 4% mutation score. Argues coverage is
  overemphasized relative to fault-detection capability.

- **Using Large Language Models to Generate JUnit Tests: An Empirical Study** —
  Siddiq et al., EASE 2024. arXiv:2305.00418.
  HumanEval: Assertion Roulette 23.8–61.3% (LLMs) vs 15% (EvoSuite) vs 0% (manual).
  Magic Number Test and Lazy Test most recurrent across all approaches.

- **On the Diffusion of Test Smells in LLM-Generated Unit Tests** — Ouédraogo et al.
  arXiv:2410.10628.
  20,500 LLM-generated suites (4 models x 5 prompting techniques) vs 780,144
  human-written suites from 34,637 projects, via TsDetect. Key finding: smells
  persist under advanced prompting, so prompt engineering alone is insufficient;
  recommends generation + automated repair/post-processing.

- **Prompt Engineering in LLMs for Automated Unit Test Generation** —
  arXiv:2407.00225 (v4, Mar 2026).
  216,300 test cases, class-level, Defects4J + SF110 + CMD (leakage-mitigating).
  Direct LLM generation compilation rates 51–78%.

### Loop-vs-instruction ablations

- **MuTAP** — Dakhel et al. Removing the iterative mutation loop caused the largest
  single drop in fault detection rate (50%).

- **Test vs Mutant: Adversarial LLM Agents for Robust Unit Test Generation** —
  arXiv:2602.08146.
  Bidirectional adversarial loop under explicit mutation-score and coverage
  thresholds; +8.56% fault detection on Defects4J.

### The null result on prompt-level intervention

- **Rethinking the Value of Agent-Generated Tests for LLM-Based Software Engineering
  Agents** — Z. Chen et al. arXiv:2602.07900 (v1 Feb 2026, v2 Apr 2026).
  Six frontier models on SWE-bench Verified. Prompt interventions that add or remove
  test-writing shift outcomes by at most ~2.6pp. AST analysis: tests are largely
  observational (value-revealing prints outnumber assertions). Efficiency effects are
  large where outcome effects are not.
  **Read as:** evidence that instructions are inert, not that testing is worthless.

### Tests-as-input (supplied, not self-generated)

- **TDFlow: Agentic Workflows for Test Driven Development** — Han et al., CMU.
  arXiv:2510.23761 (v2 Jan 2026).
  SWE-Bench Lite with human tests supplied: 88.8% vs 61.0% Agentless / 49.0%
  SWE-Agent / 48.6% ExpeRepair / 47.8% OpenHands, all on GPT-4.1. Verified: 94.3%
  with human tests ($1.01/issue) vs 68.0% self-generated ($4.12/issue). On instances
  where all self-generated tests were genuine fail-to-pass, 93.3% — i.e. human vs LLM
  authorship is not the variable; test *validity* is.
  **Caveats:** hidden SWE-bench tests are the grading criteria (circularity);
  uneven denominators (278/300 for TDFlow on Lite, 201 for OpenHands); 7
  test-hacking instances found in an 800-run manual audit, counted as failures.

- **Can Old Tests Do New Tricks for Resolving SWE Issues? (TestPrune)** —
  arXiv:2510.18270, FSE 2026.
  Cleanest ablation: agent fixed, only the regression set added. Trae-agent on
  Verified 170 → 186 resolved (+9.4%); SWE-agent on Lite 325 → 351 (+8.0%). McNemar
  tested. Cost and step count both fell.

- **TDD-Bench Verified** — Ahmed, Hirzel, Pan, Shinnar, Sinha (IBM).
  arXiv:2412.02883. 449 real GitHub issues. Best fail-to-pass rate 23.6% (GPT-4o).
  Bimodal adequacy: fail-to-pass tests exceed 90% coverage (human-comparable), the
  rest fall below 60%.

- **LLM-Based Test-Driven Interactive Code Generation (TiCoder)** — Fakhoury et al.,
  TSE / ASE 2024. arXiv:2404.10100.
  15-programmer mixed-methods study. Participants significantly more likely to
  correctly evaluate AI-generated code; significantly lower task-induced cognitive
  load.

### Mechanism argument for ordering

- **On the risk of coding before testing: An empirical study on LLM-based test
  generation workflow** — arXiv:2607.05139.
  TDD's benefit derives from independence between specification and implementation;
  code-before-test LLM workflows reintroduce dependence because tests are
  conditioned on the generated code.

### Open validity question on the gate metric

- **Do Coverage and Mutation Scores of LLM-Generated Test Suites Correlate with Their
  Effectiveness? (Replicability Study)** — arXiv:2607.22880 (late July 2026).
  Replicates Papadakis et al. on Defects4J for LLM-generated suites. Read before
  building a stopping rule on mutation score.

### Peripheral / context

- **Adoption and Impact of Command-Line AI Coding Agents** — Murphy-Hill et al.
  arXiv:2607.01418. Microsoft's early-2026 Claude Code / Copilot CLI rollout, tens of
  thousands of engineers; adopters merged ~24% more PRs than the synthetic
  counterfactual. Does *not* treat TDD as a variable.

- **Understanding Specification-Driven Code Generation with LLMs (Currante)** —
  registered report, arXiv:2601.03878. Results not yet published.
