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

## Contributing

See [HOW-TO-DEV.md](documentation/HOW-TO-DEV.md) for development setup and contribution workflow.

## Architecture

See [ARCHITECTURE.md](documentation/ARCHITECTURE.md) for cross-platform design and directory structure.
