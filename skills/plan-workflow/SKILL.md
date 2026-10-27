---
name: plan-workflow
description: Manages planning documentation through the kanban board at planning/. Use when creating, progressing, or completing plans to ensure every document lands in the right directory (planning/, in-progress/, completed/, not-implemented/, blocked/, backlog/, reminders/, waves/, evidence/, needs-review/) and stays in source control alongside the code it describes. Also covers parallel tracks — one in-progress plan per track when independent plans run in sub-agents. Pairs with plan-writing-syntax, which owns what is written inside the document; this owns where the document lives and when it moves.
metadata:
  category: workflow-composers
---

# Plan Workflow

**A plan is one piece of work.** The spec it delivers (`system-spec`) is the map, and a wave
(`wave-workflow`) runs several plans in parallel. Each can be used on its own or with the others.

This skill is the base layer of all spec-driven work. It governs how planning documents move through the kanban directories at `planning/` — whether you are working solo (simple spec-driven) or feeding into the adversarial review pipeline (complex spec-driven). Every layer of the workflow builds on top of this one; the kanban and commit discipline here apply regardless of which path you are on.

The two layers:

- **Simple spec-driven** — use this skill alone to author + execute the plan; no *plan-review* second session. The code-review caboose still applies to anything that ships (the review floor) — see the `spec-tier` skill.
- **Complex spec-driven (adversarial)** — use this skill plus `plan-send-review`, `plan-intake-review`, and `plan-receive-review`. The review trio inserts a gate into the same kanban flow before execution starts; the directories and commit discipline here still apply throughout.

**This skill owns *where a document lives and when it moves*. `plan-writing-syntax` owns *what is written on it*** — the frontmatter, the status vocabularies, and cross-references. Read that one before writing a document; read this one before moving it.

## Directory Map

```
planning/
├── backlog/           # Deferred: nobody has committed to this yet
├── (root)             # Queued: written, ready to start, nobody has started it
├── in-progress/       # Executing — one per track (see Parallel Tracks)
├── completed/         # Finished, with a ## Status block
├── not-implemented/   # Ended without being built — the second terminal state
├── blocked/           # Cannot proceed until something outside the repository clears
├── reminders/         # Tasks a person must do; completed/ underneath
├── waves/             # Scheduling documents across many plans, and the waves' context; one folder per wave allowed
├── evidence/          # Artifacts a plan produced and its Status block cites
└── needs-review/      # The review pipeline's copies — never the plan of record
```

The first six are **the board**, and they hold plans and stubs alike. **A stub is a baby plan**
(`plan-writing-syntax`): named differently so you can
tell them apart at a glance, but moving through the same columns under the same rules. The folders
are status buckets — they say *what happened to the document*, never *what kind of document it is*.

So `backlog/` does not mean "where stubs live". It means **deferred**, and a plan that was worked
and then outranked belongs there exactly as much as an idea captured mid-session does. Before
stubs became baby plans, such a plan went back to the queue root, where it was a permanent false positive on "what
is ready to start".

The last four are not the board. A reminder is work for a *person*; a `needs-review/` copy is
explicitly never the document of record; a wave schedules across many plans rather than being
scheduled itself; `evidence/` holds artifacts, not documents. They are different kinds of thing,
not lifecycle states, which is why they keep their own vocabularies.

Every directory is committed to source control. **No document is ever deleted — only moved.**

That rule has been broken once, and it is worth knowing how: a stub was deleted when its plan was promoted, which left the plan pointing at a file that did not exist. Nobody noticed for a week. Promotion is not deletion — move the stub to `completed/` and let it stand as the record of when the problem was first seen.

### `not-implemented/` — the second terminal state

For a plan that **built nothing**: superseded, folded into another plan, obsoleted, or tried and abandoned.

It exists because both alternatives lie. `completed/` is wrong — a reader counting it to see what the system does would count a plan that did not. The queue root is worse: it means *ready to start*, so a dead plan sitting there is a permanent false positive on "what is next".

**An abandoned approach is knowledge.** The next person to have the same idea should find the document saying it was tried and why it was rejected, which is the whole reason the document is kept rather than dropped.

### The directory *is* the status, and both are written down

A document's `status:` field and the directory it sits in have to agree. That is redundancy on purpose — `ls planning/backlog/` reading as a status board is a real affordance a field alone does not give back — and where a repository has a validator, disagreement is a build failure rather than a discovery six weeks later.

**So a move is two edits, not one:** `git mv` the file, and change its `status:` to match. Doing one without the other is the drift this arrangement exists to catch.

---

## Decision Tree: Where Does This Plan Go?

```
Did plan mode just produce a new plan?
│
├─ YES — Was a document already written to planning/?
│         │
│         ├─ YES → Leave it in planning/. It is queued and ready.
│         │
│         └─ NO  → Write it directly to planning/in-progress/.
│                  (Exiting plan mode into immediate execution skips the queue.)
│
└─ NO — Is this an update to an existing plan?
          │
          ├─ Execution finished successfully →
          │    1. Append a ## Status section to the document (see template below).
          │    2. Move the document from planning/in-progress/ to planning/completed/.
          │    3. If it has delivers:, set each feature it names to Shipped or
          │       Shipped interim in the spec, and run spec-table --fix (system-spec).
          │
          ├─ Execution interrupted / not completed →
          │    1. Append a ## Status section noting where work stopped.
          │    2. Move the document back to planning/ (the queue root).
          │
          ├─ Ended without being built →
          │    1. Append a ## Status section saying which of the four reasons, and
          │       naming what it points at (superseded / folded-in / obsoleted /
          │       abandoned — see plan-writing-syntax).
          │    2. Move the document to planning/not-implemented/.
          │
          ├─ Cannot proceed until something outside the repository changes →
          │    1. Record what is being waited on, by name.
          │    2. Move the document to planning/blocked/.
          │
          ├─ Worked, but something else now outranks it →
          │    1. Append a ## Status section saying where work stopped.
          │    2. Add a Trigger for Promotion: what should pull it back.
          │    3. Move the document to planning/backlog/.
          │
          └─ Item discovered during planning, outside current scope →
               Write a new stub to planning/backlog/ (see Backlog section).
```

**Every one of those moves changes the `status:` field too.** See *The directory is the status* above.

---

## File Naming

**The prefix carries `kind`:** `stub_<name>_<YYYY-MM-DD>.md` for a stub, and one of three for a plan:

| Plan prefix | For | Name (what references use) |
|---|---|---|
| `feature_<name>_<YYYY-MM-DD>.md` | new behaviour | `<name>_<YYYY-MM-DD>` |
| `bug_<name>_<YYYY-MM-DD>.md` | a fix | `<name>_<YYYY-MM-DD>` |
| `wave-<n>_<track>[-<name>]_<YYYY-MM-DD>.md` | a track's plan in wave *n* (`wave-workflow`) | the whole filename minus `.md` |

```
planning/backlog/stub_session-token-storage_2026-06-22.md   # an idea, deferred
planning/feature_auth-refactor_2026-06-22.md                # queued
planning/in-progress/feature_auth-refactor_2026-06-22.md    # running
planning/completed/feature_auth-refactor_2026-06-22.md      # shipped
planning/completed/stub_session-token-storage_2026-06-22.md # promoted
planning/bug_token-expiry-off-by-one_2026-06-23.md          # a fix, queued
planning/wave-1_read_2026-10-26.md                          # wave 1's read track
```

`wave-<n>_` stays in the name, so every reference to the plan says which wave it belongs to, and a wave's plans sort together in whichever directory they sit. The validator reports `UNKNOWN-WAVE` when `waves/wave-<n>/` holds no `kind: wave` document.

**The filename never changes.** Not when the document moves, not when it is promoted — kind does not change, so neither does the prefix. The name is the document's identity and the directory is its status, which is what lets another document reference it by name and go on resolving after it moves.

The prefix used to carry *status* (`feature_` → `promoted_` → `closed_`), which renamed the file on every move. That is retired: `kind:` was already doing the job, and a filename that changes is drift waiting to happen.

Two rules, deliberately not symmetric:

- **A stub must carry `stub_`.** It shares every directory with plans now, so the prefix is the only thing distinguishing the two at a glance.
- **A plan need not carry `feature_`, `bug_` or `wave-<n>_`.** A missing prefix is a lost affordance, not a lie — documents predating the convention read fine without it, and renaming a completed document to satisfy a new rule is what *the name is the identity* forbids. A prefix belonging to *another* kind is always an error.

---

## Plan Document Structure

**The frontmatter block belongs to `plan-writing-syntax`** — `kind`, `status`, `track`, `date`, and whatever the status requires. Read that skill for the vocabularies; this is only where it sits.

Under it, a plan's body:

```markdown
---
kind: plan
status: queued
track: main
date: YYYY-MM-DD
---

# [Feature / Bug / Task Name]

**Author:** [name or agent]

## Objective
One sentence: what this plan achieves when done.

## Success Criteria
- [ ] Criterion 1
- [ ] Criterion 2

## Approach
How the work will be done. Include files to be modified and any dependencies.

## Phases
### Phase 1 — [Name]
[What to do. Mark async-eligible steps with [ASYNC].]

### Phase 2 — [Name]
...

## Dependencies / Prerequisites
List blocking work or required context.

## Files Expected to Change
- path/to/file.ext — reason
```

---

## Status Section (appended whenever a plan leaves `in-progress/`)

Append this block to the document **before moving it**, and update the frontmatter `status:` in the same edit.

**The section carries the reasoning; the frontmatter carries the facts.** `status:`, `track:` and `completed:` are fields, and `plan-writing-syntax` owns them — a `**Final Status:**` line repeating one here is the drift that contract exists to end. What belongs below is the half no field can hold.

**Every departure from `in-progress/` gets one, not only the terminal ones.** A plan returned to the queue half-done is exactly the one whose Status section matters most — it is the only record of where the work stopped and what the next session is walking into. Its `status:` goes back to `queued`; the section says why it is not further along.

```markdown
---

## Status

**Completed By:** [name or agent]

### Outcomes
- What was actually delivered (may differ from plan).

### Deviations
- Any departure from the original approach, and why.

### Lessons Learned
- Non-obvious findings worth carrying forward.

### Backlog Items Created
- <stub-name> — one-line summary
```

**Write it in the minutes after finishing, not from a diff a week later.** The deviations and the lessons are the half nothing else in the repository records, and they are the half that is reconstructed worst.

**A completed plan is not a to-do list.** Work that was in scope and did not land leaves as a backlog stub with a name, not as a "still to do" section inside a document filed under `completed/`.

---

## Backlog Items

When planning or execution uncovers work that is real but out of scope for this session, write a stub to `planning/backlog/`. Do not attempt to execute it now.

Minimum content for a backlog stub:

```markdown
---
kind: stub
status: backlog
date: YYYY-MM-DD
---

# [Item Name]

**Discovered During:** <source-plan-name>_<date>

## Context
Why this item exists and what problem it solves.

## Known Scope
What is understood so far. Leave blank if unknown.

## Trigger for Promotion
What event or decision should pull this into active planning.
```

Three fields and nothing else is required, on purpose: **capture has to stay cheap or ideas go uncaptured.**

Stubs stay in `planning/backlog/` until a future session promotes them: read the stub, write the plan — from the harness planning document, not by growing the stub — and put **the plan** in `planning/` (queued) or `planning/in-progress/` (immediate start).

**Promotion does not delete the stub, and it produces two documents.** The stub is the record of when the problem was first seen; the plan is the record of what was done about it, and they are different facts.

| the stub's fate | status | directory | says |
|---|---|---|---|
| a plan grew out of it | `completed` | `planning/completed/` | `completed:`, optionally `promoted_to:` |
| it turned out to be a bad idea | `not-implemented` | `planning/not-implemented/` | `reason:` and what that reason names |

**Promotion *is* a stub's completion.** Its job was to hold the idea until someone committed to it, and being promoted is that job finished. `not-implemented/` is for a document whose idea went nowhere; filing every successful stub there would drown the directory whose whole value is that a rejected approach stays findable.

The cost, stated plainly: `completed/` now holds both shipped plans and promoted stubs. Count by kind — `grep -l "^kind: plan" planning/completed/*.md` — rather than by directory.

**A stub is not confined to those two endings.** It can be `queued`, it can be `blocked`, and if someone works a baby plan directly it takes a track in `in-progress/` and ships like anything else. Nothing is illegal for a stub.

---

## Parallel Tracks

`in-progress/` holds **one document per track**, not one overall. A track is a
concurrency slot — a channel of work with a single executor — and everything in
`in-progress/` declares which one it runs on, a stub being worked directly included:

```yaml
track: main
track: content-areas
```

`main` is the foreground session: the default, and the value to use whenever no
sub-agent is involved. Any other value names a track a sub-agent owns for the duration
of the plan.

Name the track after the **work**, not the worker. Sub-agents are ephemeral — one dies,
another resumes the same plan — so an agent id in this field goes stale the moment the
agent does. A stable slug (`engine`, `content-areas`, `infra`) survives handoff and
still discriminates cleanly.

### Admitting a document to a track

A plan — or a stub being worked directly — may move into `in-progress/` when both hold:

1. **The track is free.** No other document in `in-progress/` declares the same track.
2. **Its file set is disjoint.** Its `## Files Expected to Change` list shares no path
   with any other in-progress document's list.

Rule 2 is the one that actually protects the work. The track field is bookkeeping; two
agents editing the same file is the failure it exists to prevent. If the file sets
overlap, the two are not independent. With two or three plans, queue the second in
`planning/` instead of splitting it across tracks. With more, write a wave
(`wave-workflow`): it keeps the sets disjoint by naming an owner for the shared file,
splitting it per track, or running the change as a gate first.

```bash
# What is running, and where
grep -H "^track:" planning/in-progress/*.md

# Does the file set collide with anything already running?
grep -hA20 "## Files Expected to Change" planning/in-progress/*.md | grep "^- "

The greps above are a quick eyeball. **The check is `scripts/plan-doc.cs validate`**, which parses
`track:` and the file-set lists rather than grepping them, and reports both collisions by name:

```powershell
dotnet run "$plan\plan-doc.cs" -- validate --path planning
```

It is the authority where it and this page disagree — read its source and fix the page.

### Committing from a track

Each track commits only its own plan document and the code that plan names. Do not
restage another track's files while it is mid-flight — the kanban move and the code it
describes belong in the same commit, and that commit belongs to one track.

### Returning a plan to the queue

An interrupted plan keeps its `track:` field — that is the record of where it ran — but
the value is advisory once the document is back in `planning/`.
Reassign it when the plan restarts; the track it used before may be busy or gone.

---

## Adversarial Review Integration (Optional)

When using the plan review pipeline (`plan-send-review`, `plan-intake-review`, `plan-receive-review`), insert a review gate **before** execution starts:

```
planning/ (queued plan)
    │
    ▼
planning/needs-review/      ← plan-send-review deposits here
    │
    ▼
planning/needs-review/reviewed/  ← plan-intake-review returns reviewed plan here
    │
    ▼
Read review feedback → address critical issues → update plan
    │
    ▼
planning/in-progress/       ← now begin execution
```

When used without the review pipeline, move directly from `planning/` to `planning/in-progress/` when execution starts.

---

## The scripts

The skill ships `scripts/plan-doc.cs`, one file with three verbs, sharing one reading of the
frontmatter contract on purpose — `close` prevents one class of status/directory drift and
`validate` reports every class, so they must not disagree about what the contract says.
`spec-table` generates a system spec's status table (see `system-spec`); it lives here because
resolving a plan's `delivers:` needs plans and specs read by the same parser.

```powershell
# Set once per session — works whether installed via plugin or as a user skill
$plan = if ($env:CLAUDE_PLUGIN_ROOT) {
  "$env:CLAUDE_PLUGIN_ROOT\skills\plan-workflow\scripts"
} else {
  "$HOME\.claude\skills\plan-workflow\scripts"
}

# Report every contract violation across the board; exit 1 on any
dotnet run "$plan\plan-doc.cs" -- validate --path planning

# Make the move atomic: rewrite status: AND git mv, or do neither
dotnet run "$plan\plan-doc.cs" -- close --doc planning/in-progress/feature_x_2026-09-14.md `
    --to completed --status-block ./statusblock.md

dotnet run "$plan\plan-doc.cs" -- close --doc <path> --to blocked --blocked-on "the thing being waited on"
dotnet run "$plan\plan-doc.cs" -- close --doc <path> --to not-implemented `
    --reason superseded --reason-target some-other-plan_2026-09-14

# Promoting a stub is closing it to completed. The filename does not change.
dotnet run "$plan\plan-doc.cs" -- close --doc planning/backlog/stub_an-idea_2026-09-10.md `
    --to completed --promoted-to the-plan-it-became_2026-09-16 `
    --decided-by "A Name" --decided-on 2026-09-16

# System specs (system-spec): check plans' delivers: against the specs, and the specs themselves
dotnet run "$plan\plan-doc.cs" -- validate --path planning --specs docs

# Regenerate each spec's status table from its feature lines and the plans that deliver them
dotnet run "$plan\plan-doc.cs" -- spec-table --specs docs --path planning --fix

dotnet run "$plan\plan-doc.cs" -- --selftest    # 186-case battery; exit 0 iff all pass
```

`--dry-run` prints the new frontmatter and the destination without touching anything.

### What `close` refuses

Each refusal is a document that would otherwise lie about itself:

- a status whose required field is missing (`completed` without `completed:`, `blocked` without
  `blocked_on:`, `not-implemented` without `reason:` **and** the field that reason must name)
- a `--decided-on` that is not ISO `YYYY-MM-DD`
- a `--reason-target` shaped like a path rather than a name
- a document that already carries a `## Status` section — it would append a second
- a document whose `status:` and directory **already** disagree; something upstream is broken and
  this is not the tool to paper over it

### The `git add` that looks redundant and is not

`close` runs `git add` on the destination after `git mv`. That is load-bearing: **`git mv` stages
the rename from the blob already in the index**, so the frontmatter rewrite — a working-tree change
never added — is left unstaged, and the commit moves the file while committing its *old* content.
Observed for real: a document landed in `completed/` still saying `status: in-progress`, which is
the precise drift this script exists to prevent, shipped by the script's own move.

That line is covered by `test/Invoke-CloseIntegrationCheck.ps1`, not by `--selftest`. It is a property
of the git index, which no pure battery reaches — and a mutant deleting the `git add` **survived**
the battery, which under `test-filter-development` rejects the test rather than the mutant.

```powershell
.\test\Invoke-CloseIntegrationCheck.ps1     # 24 checks against a throwaway repo, exit 0 required
```

PowerShell 7, matching the other PowerShell checks in this collection — not bash, so it runs on Windows without Git Bash and on Linux and macOS alike.

## Commit Checkpoints

Commit the plan document at each state transition:

| Event | Commit message prefix |
|---|---|
| Plan written to `planning/` | `[PLAN]` |
| Plan moved to `planning/in-progress/` | `[PLAN]` |
| Feature completed (code + plan status updated) | `[FEATURE]` |
| Feature moved to `planning/completed/` | `[FEATURE]` |
| Plan moved to `planning/not-implemented/` | `[PLAN]` |
| Plan moved to `planning/blocked/` | `[PLAN]` |
| Backlog item created | `[BACKLOG]` |

Example: `[FEATURE] auth-refactor phase 1 complete — JWT validation extracted`

---

## Guardrails

- **One active document per track** in `planning/in-progress/`. Most work is `track: main`, so most of the time this reads as one active plan. If something must start urgently on a track that is already busy, move the current document back to `planning/` with a status note first.
- **Tracks must be independent.** A document only earns a track of its own if its `Files Expected to Change` set is disjoint from every other in-progress document's. Overlapping work queues, or a wave resolves the overlap (`wave-workflow`); it never runs in parallel as is.
- **Never delete plan documents.** Move them; the history is the corpus. **Promotion is not deletion** — deleting a stub on promotion is how one plan came to point at a file that did not exist.
- **A move is two edits.** `git mv`, and the `status:` field. One without the other is exactly the drift the field was introduced to end. It is never three: the filename does not change.
- **Backlog is not a graveyard.** Each stub should have a `Trigger for Promotion` — if it will never be acted on, move it to `not-implemented/` with a reason rather than leaving it deferred forever.
- **All directories are source-controlled.** Add `.gitkeep` files if directories would otherwise be empty.
- **`needs-review/` never holds the plan of record.** Its contents are copies for the review pipeline; the plan itself stays on the board.
