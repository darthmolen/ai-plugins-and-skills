# Planning Vocabulary

This page defines the terms the planning skills in this plugin use. When a skill, plan or review
uses one of these words, it means what this page says. The reasons behind the less obvious choices
are at the end, under [Why these words](#why-these-words).

## How specs, feature-specs and plans relate

```text
system spec   one per area, evergreen: what the system is, and what has shipped
     ^
     |  merge: agree | amend + why | add
     |
feature-spec  a section of a plan: the piece of spec this plan changes
     ^
     |
plan          the work: how and why for one delivery; history once completed
```

Here, "spec" means either a system spec or a feature-spec. It describes the system's target
behaviour. A plan describes the work that gets the system there.

### Changing something a completed plan delivered

A developer asks: "Can I make change X without changing the main plan?"

Yes. Each change gets its own plan, with its own feature-spec. Completed plans are history and are
never edited. The new plan's feature-spec amends the system spec, and that merge lands in the same
pull request as the code.

## Terms

| Term | Is | Lives in | Owned by | Is not |
|---|---|---|---|---|
| **System spec** | The description of the target system for one area, at full detail, with each feature's status | `docs/specs/`, `kind: spec` | `system-spec` | A plan, or a copy per release |
| **Feature** | A section of a system spec that opens with a feature line (`**Feature:** <slug> · **Status:** <status>`) | A system spec | `system-spec` | Any heading. One without a feature line is ordinary prose |
| **Feature-spec** | The section of a plan that states the plan's change to a system spec. Each block names its target feature and its merge kind: `agree`, `amend` (with why), or `add` | The plan's `## Feature-spec` section | `plan-writing-syntax` | A replacement for the system spec. It merges into it |
| **Plan** | The work for one delivery: approach, decisions, tests, and outcome | `planning/` and its board columns | `plan-workflow`, `plan-writing-syntax` | A spec. Once completed, it is a record that is never edited |
| **Stub** | A plan nobody has committed to yet, captured with enough context to pick up later | `planning/backlog/` usually, as `stub_<name>_<date>.md` | `plan-workflow` | A separate system. It moves through the same columns as a plan |
| **Brief** | The prompt file that tells an agent what to do in one session | Wherever the workflow keeps it, often beside the plan | [SPEC-DRIVEN-CODE-WORKFLOW.md](workflows/SPEC-DRIVEN-CODE-WORKFLOW.md) | A plan or a spec. It points to them |
| **Tier** | How much detail a plan needs and which reviews it must pass: `low`, `medium` or `high`, set by stakes | The plan's `tier:` field | `plan-tier` | A measure of effort. It measures the cost of being wrong |
| **Board** | The directories under `planning/`. The directory a document sits in is its status | `planning/` | `plan-workflow` | A tool. It is directories under source control |
| **Wave** | A set of plans run in parallel across tracks, under one lead session | `planning/waves/` | `wave-workflow` | A release |
| **Track** | One line of work in a wave, or on the board; `in-progress/` holds one plan per track | A plan's `track:` field | `plan-workflow`, `wave-workflow` | A branch, though a track often has one |
| **Session** | One agent working in one worktree of a repository on one machine, with a subject and a mode. It starts through `session-workflow start` and ends at every stop | A session file in `~/.agent-sessions/`, never committed | `session-workflow` | A chat or harness session. It is the record other sessions on the machine read |
| **Mode** | The consent a session runs under: `plan`, `implement`, `commit`, `ship` or `lead`. Each allows its own changes for the session's subject and nothing wider | The session file's `mode` field | `session-workflow`; a driver such as `ticket-start` picks it | A permission setting of the harness. Holding to it is the agent's job |
| **Subject** | What a session works on, as `<kind>:<value>`: `ticket:22744`, `plan:<name>`, `wave:wave-<n>/<track>` or `wave:wave-<n>/lead`. One repository has one live session per subject | The session file's `subject` field | `session-workflow`; a driver such as `ticket-start` picks it | A context. The AI's context window and the waves context document are unrelated |
| **Driver** | Whatever starts a session and names its subject and mode: `ticket-start`, a plan you run yourself, or a wave's lead and track instructions | Outside `session-workflow`, which knows nothing about tickets or waves | The driving skill | Part of the session layer |
| **Lock** | A claim one session holds on a resource shared only while something runs, such as a port, local database or container, named from its address (`port-5000`) | `~/.agent-sessions/locks/` | `session-workflow` | A file-set check. Collisions on files are prevented when a plan is admitted to a track |
| **Slice** | One unit of a code review: a commit range with its focus areas and the plans it implements | `planning/code-reviews/`, `kind: slice` | `code-send-review`, `code-review-seed-slices` | A plan or a spec |
| **Synthesis** | One initiative's consolidated list of open code-review findings. Each run writes the next numbered file and supersedes the last | `planning/code-reviews/`, `kind: synthesis` | `code-synthesize-reviews` | A report that is rewritten in place. Syntheses are append-only |
| **Reminder** | A task a person must do that no test or gate can catch, including a check that can only happen after the pull request (a `verify` reminder) | `planning/reminders/` | `reminders-set` | A plan phase or a backlog stub |
| **ADR** | A record of an architectural decision, its alternatives and its consequences | Your project's decision records, if it keeps them | Your project | A plan. It records why, not the work |
| **Evidence** | An artifact a plan produced and its Status block cites: output, screenshots, measurements | `planning/evidence/` | `plan-workflow` | A planning document. It carries no frontmatter |
| **Interim** | A reduced form of a feature that ships before the target. It is recorded in an `Interim` block inside the feature | A system spec | `system-spec` | A trimmed target. The full target text stays |
| **Change kind** | How an interim relates to its target: `UI only`, `Contract subset`, or `Migration` | An interim block | `system-spec` | A merge kind. That belongs to a feature-spec |
| **`delivers:`** | The plan field that lists the features it delivers, as `<spec>#<slug>` | Plan frontmatter | `plan-writing-syntax` | A path. It is a name |

A plan completes at its pull request. The close writes the Status block, moves the plan to
`completed/`, and turns any check that can only run after the pull request into a `verify`
reminder, all in the same pull request as the code.

## Skill names: architect, developer, engineer

A language or framework skill is named `<subject>-<role>`, and the role says which one to load:

| Role | Covers | Examples |
|---|---|---|
| `-architect` | Project setup, tooling, layout, and design decisions made before or across the code | `angular-architect`, `python-architect` |
| `-developer` | Writing application code: idioms, patterns, and pitfalls | `angular-developer`, `python-developer`, `csharp-quality-developer` |
| `-engineer` | Infrastructure and platform work: provisioning, networking, identity, infrastructure as code, pipelines | `azure-infra-engineer` |

The test for `-developer` versus `-engineer` is what the skill's output runs as. Code that ships
inside an application is `-developer`. Code that builds, deploys or provisions the environment the
application runs in (Bicep, Az PowerShell, pipeline YAML) is `-engineer`. A subject can have a
narrower `-developer` sibling when its code splits into distinct kinds: `python-data` sits beside
`python-developer` because dataframe and notebook code shares little with a web service beyond
syntax.

Tool skills are named for what they do (`sql-query`, `bulk-rename`), and workflow skills by the
process they run (`plan-workflow`, `session-workflow`).

## Terms that used to mean something else

| Old wording | Now |
|---|---|
| `spec-tier`, "spec depth", "spec-as-contract" | `plan-tier`, plan depth, plan-as-contract |
| "slice spec" | slice |
| "write the spec", meaning the prompt file | write the brief |
| `angular-expert`, `python-pro`, `react-specialist`, `sql-pro`, `typescript-pro` | `angular-developer`, `python-developer`, `react-developer`, `sql-developer`, `typescript-developer` |

`spec-tier` still loads, as a deprecated shim that points to `plan-tier`. The other old skill names
were removed with no alias.

External specifications, such as OpenAPI or the Agent Skills format, keep their own names.

## Why these words

### "Spec" means two things, and nothing else

"Spec" used to mean four things: the system spec, the plan (in "spec depth"), the prompt file, and
one unit of a code review ("slice spec"). A reader could tell which only from context, and the
question "can I change X without changing the main plan?" had no clear answer. Now "spec" is only
the system spec or a plan's feature-spec, and the other three have their own names: plan, brief and
slice.

The name "feature-spec" says where the section goes next: it merges into the system spec, feature
by feature. A "design" section would not say that it has a destination, and "plan spec" would bring
the ambiguity back.

`spec-tier` became `plan-tier` because the skill sets how much detail a plan needs. It is kept as a
shim whose description says only that the name is deprecated. That description names no task, so
the shim does not compete with `plan-tier` to load, and prompts and instruction files that still
say `spec-tier` keep working.

### Role suffixes instead of -pro, -expert and -specialist

The old suffixes told a reader nothing: `-expert`, `-pro` and `-specialist` all meant "how to write
the code". Once a subject had two skills, one that sets the project up and one that writes the code
in it, the name had to say which was which. The renamed skills keep no alias, because an alias is a
second skill with the same description, and the two would compete to load.

### A session layer between plans and waves

Several agents on one machine collide in ways the board cannot see: two test runs on one port, one
worktree removed while another session's server runs from it, a shared git configuration written
from inside a worktree. `plan-workflow` prevents collisions it can check before work starts, by
admitting a plan to a track only when its files are disjoint from every running plan's.
`session-workflow` handles what is shared only while something runs, with locks, and records each
session's subject and mode so the others can see it.

The session layer sits under both a ticket and a wave rather than above them. A composer above
`ticket-start`, wave tracks and ad-hoc branches would be a fourth workflow family beside plans,
waves and code reviews, and its one job, starting and ending the work, is what `session-workflow`
does without sitting above anything. Every way of working then ends the same way: the plan closes,
then the pull request opens.

A session's **subject** is not called its "context", because "context" already meant the AI's
context window and the waves context document.
