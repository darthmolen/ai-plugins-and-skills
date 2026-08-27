# AI-Plugins-And-Skills

Cross-platform AI coding assistant skills following the [Agent Skills](https://agentskills.io) open specification — they work across Claude Code, Copilot CLI, and VS Code Copilot.

## Supported Platforms

| Platform | Status | Guide |
|----------|--------|-------|
| [Claude Code](documentation/README-CLAUDECODE.md) | Fully supported | Installation, commands, and usage |
| [Copilot CLI](documentation/README-COPILOT-CLI.md) | Skills supported | Installation and current limitations |
| [VS Code Copilot](documentation/README-VSCODE-COPILOT.md) | Skills supported | Installation and usage |

## Skills

Skills are situational knowledge files an AI coding harness loads when their description matches what you're doing — repeatable processes, agent-callable tools, or domain knowledge. See [skills/README.md](skills/README.md) for the longer intro and per-harness install guides.

### Review Helpers

<!-- write category flavor text here; the table below is auto-generated -->

Local, adversarial review workflows — first the plan (or spec), then the code commits.

For planning: run [plan-send-review](skills/plan-send-review/) in your authoring session, bounce to a second session (or a different agent) for [plan-intake-review](skills/plan-intake-review/), then return to the original session and run [plan-receive-review](skills/plan-receive-review/) to read the feedback.

For code commits, pick a path: one large ad-hoc review with [code-send-review](skills/code-send-review/), or slice-and-dice a branch into multiple slices with [code-review-plan-create](skills/code-review-plan-create/) that will then get reviewed in parallel and rolled into a single pre-merge action list via [code-synthesize-reviews](skills/code-synthesize-reviews/).

See [documentation/workflows/](documentation/workflows/README.md) for the full plan-review and code-review workflows — state machines, directory layouts, and the spec-driven coding workflow that ties them together.

<!-- SKILLS: review-helpers -->
| Skill | Description |
|-------|-------------|
| [plan-send-review](skills/plan-send-review/) | Use when you have authored a plan and want the other AI agent (Claude or Copilot) to review it - ... |
| [plan-intake-review](skills/plan-intake-review/) | Use on a loop interval to check for plans awaiting review in planning/needs-review/, move to in-p... |
| [plan-receive-review](skills/plan-receive-review/) | Use when a plan you authored has been reviewed and is waiting in planning/needs-review/reviewed/ ... |
| [code-send-review](skills/code-send-review/) | Use when queueing a slice of a branch for code review — builds a slice spec from a commit range, ... |
| [code-intake-review](skills/code-intake-review/) | Use on a loop interval to check for code slices awaiting review in planning/code-reviews/needs-re... |
| [code-review-apply](skills/code-review-apply/) | Use when reviewed code-review slices are ready for triage and the developer wants to walk finding... |
| [code-review-plan-create](skills/code-review-plan-create/) | Use when ready to plan a code review of a development branch — walks the branch, discovers existi... |
| [code-review-seed-slices](skills/code-review-seed-slices/) | Use when a code-review meta-plan is finalized and you need to fan it out into one slice spec per ... |
| [code-review-plan-execute](skills/code-review-plan-execute/) | Use when a code-review meta-plan is ready and you want to drive the full review pipeline — pre-fl... |
| [code-synthesize-reviews](skills/code-synthesize-reviews/) | Use when multiple code-review slices have been reviewed and you need a consolidated pre-merge act... |
<!-- END SKILLS: review-helpers -->

### Tools

<!-- write category flavor text here; the table below is auto-generated -->

When a workflow is known up front, a curated tool almost always beats ad-hoc agent queries on tokens. The skills below ship both Python and single-file C# variants; the Python ones just run, but the C# ones need a working .NET environment:

1. [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) installed
2. [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) installed — or any other credential source [`DefaultAzureCredential`](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential) will pick up
3. Authenticated via `az login`

<!-- SKILLS: tools -->
| Skill | Description |
|-------|-------------|
| [codebase-mapper](skills/codebase-mapper/) | Generate a deterministic architecture map of Python, C#, and TypeScript codebases using AST parsing |
| [ace-fca-workflow](skills/ace-fca-workflow/) | Advanced Context Engineering with Frequent Intentional Compaction (ACE-FCA) for complex coding tasks |
| [azure-servicebus-operations](skills/azure-servicebus-operations/) | Use when peeking, draining, snapshotting metrics on, enumerating, or otherwise inspecting/manipul... |
| [bulk-rename](skills/bulk-rename/) | Use when a refactor needs the same identifier rename applied across many files (type-cluster rena... |
| [psql-query](skills/psql-query/) | Use when running ad-hoc read-only SQL against Azure Database for PostgreSQL — exploring tables, s... |
| [sql-query](skills/sql-query/) | Use when running ad-hoc read-only T-SQL against Azure SQL — exploring Query Store, sampling table... |
<!-- END SKILLS: tools -->

### Language Helpers

<!-- write category flavor text here; the table below is auto-generated -->

Plenty of general-purpose language helpers exist already, so no need to duplicate. These are about keeping and enforcing standards. If you already lean on [EditorConfig](https://editorconfig.org/) and [StyleCop](https://github.com/DotNetAnalyzers/StyleCopAnalyzers) for your C# work, the csharp skill slots in naturally. The Python skill bakes in the usual suspects — [ruff](https://docs.astral.sh/ruff/), [pyright](https://microsoft.github.io/pyright/), pre-commit hooks — for enforcing good script hygiene.

<!-- SKILLS: language-helpers -->
| Skill | Description |
|-------|-------------|
| [csharp-quality-developer](skills/csharp-quality-developer/) | Enforce C# coding standards including StyleCop rules (SA1028, SA1518, SA1101, etc.), file formatt... |
| [python-quality-developer](skills/python-quality-developer/) | Enforce Python coding standards including ruff rules, pyright type safety, avoiding Any, consiste... |
| [python-quality-install](skills/python-quality-install/) | Use when initializing a new Python project or adding quality tooling to an existing one - sets up... |
<!-- END SKILLS: language-helpers -->

### Workflow Composers

<!-- write category flavor text here; the table below is auto-generated -->

Where the review helpers above handle one review, these decide how much process a piece of work deserves in the first place — and then hold you to it. Start with [spec-tier](skills/spec-tier/) to route a task to a Low/Medium/High spec depth by stakes, let [plan-workflow](skills/plan-workflow/) move the resulting document through the `planning/` kanban, and use [test-filter-development](skills/test-filter-development/) instead of plain TDD when an agent is writing the code — it makes the test filter load-bearing by demanding captured RED output and a seeded mutant rather than a claim that the tests were written first.

<!-- SKILLS: workflow-composers -->
| Skill | Description |
|-------|-------------|
| [plan-workflow](skills/plan-workflow/) | Manages planning documentation through the kanban board at planning/ |
| [prompt-dotnet](skills/prompt-dotnet/) | Composes and executes a standardized, spec-driven, test-first .NET feature prompt |
| [spec-tier](skills/spec-tier/) | Route a task to a Low/Medium/High spec tier by stakes, then enforce the matching spec depth, revi... |
| [test-filter-development](skills/test-filter-development/) | Use when implementing any feature or bugfix with AI assistance, before writing implementation code |
<!-- END SKILLS: workflow-composers -->

### Domain Skills

<!-- write category flavor text here; the table below is auto-generated -->

Stack-specific expertise the agent loads when it recognizes the stack — framework idioms, current API surface, and the mistakes that stack in particular invites. Unlike the language helpers above, these are not about enforcing a house style; they are about knowing the material.

<!-- SKILLS: domain-skills -->
| Skill | Description |
|-------|-------------|
| [angular-architect](skills/angular-architect/) | Expert Angular architect mastering Angular 20+ with enterprise patterns |
| [angular-expert](skills/angular-expert/) | Angular 20+/TypeScript frontend expert |
| [azure-infra-engineer](skills/azure-infra-engineer/) | Azure cloud infrastructure expert specializing in network design, identity integration, PowerShel... |
| [dotnet-secrets](skills/dotnet-secrets/) | Secrets hygiene for .NET development |
| [python-pro](skills/python-pro/) | Python backend expert |
| [react-specialist](skills/react-specialist/) | React best practices expert |
| [sql-pro](skills/sql-pro/) | Expert SQL developer specializing in complex query optimization, database design, and performance... |
| [typescript-pro](skills/typescript-pro/) | TypeScript best practices expert |
<!-- END SKILLS: domain-skills -->

## Curriculum — a separate, opt-in plugin

The `curriculum-*` skills are a seven-stage pipeline that turns source documentation into course definitions and course artifacts: discover → classify → extract → design → govern → generate, with the governance gate running twice (once on the definition, once on what was rendered from it).

**They ship as their own plugin and are deliberately not part of the main one.** Every installed skill's description sits in your context for the whole session whether you use it or not, and this pipeline is inert unless you have a curriculum workspace for it to read. Making you opt in keeps that cost off everyone else.

```bash
claude plugin install curriculum@ai-plugins-and-skills-ai-standards
```

Add `--scope project` to enable it only inside the repo that holds your curriculum workspace, rather than globally.

Start at [curriculum-orchestrate](plugins/curriculum/skills/curriculum-orchestrate/) — it documents the workspace layout the whole family expects, and [curriculum-discover](plugins/curriculum/skills/curriculum-discover/) carries a `sources.example.yaml` to copy.

<!-- SKILLS: curriculum -->
| Skill | Description |
|-------|-------------|
| [curriculum-classify](plugins/curriculum/skills/curriculum-classify/) | Use when sorting a curriculum source manifest into content domains and teachability — deciding wh... |
| [curriculum-design](plugins/curriculum/skills/curriculum-design/) | Use when turning extracted learning units into course definitions — deciding course boundaries, l... |
| [curriculum-discover](plugins/curriculum/skills/curriculum-discover/) | Use when building a source manifest for curriculum generation — enumerating eligible files in a r... |
| [curriculum-extract](plugins/curriculum/skills/curriculum-extract/) | Use when converting classified curriculum sources into traceable learning units — concepts, outco... |
| [curriculum-generate](plugins/curriculum/skills/curriculum-generate/) | Use when rendering approved course definitions into artifacts — student guide, instructor guide, ... |
| [curriculum-govern](plugins/curriculum/skills/curriculum-govern/) | Use when validating curriculum before generation or publication — schema, source fidelity, proven... |
| [curriculum-orchestrate](plugins/curriculum/skills/curriculum-orchestrate/) | Use when generating or refreshing course curriculum from source repositories — turning documentat... |
<!-- END SKILLS: curriculum -->

## Contributing

See [HOW-TO-DEV.md](documentation/HOW-TO-DEV.md) for development setup and contribution workflow.

## Architecture

See [ARCHITECTURE.md](documentation/ARCHITECTURE.md) for cross-platform design and directory structure.
