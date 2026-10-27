---
name: wave-workflow
description: Use when several plans need to run in parallel across tracks under one lead session — setting up a wave, starting its lead and track sessions (`lead` or `track <name>` as arguments), deciding which files each track owns and what a track does when it needs another's file, running the wave (pause points, contracts that freeze on a named day, the lead pushing verified commits), the integration window, and closing it. Sits above plan-workflow; it schedules plans and never rewrites them.
metadata:
  category: workflow-composers
---

# Wave Workflow

**A wave runs plans in parallel.** Each plan (`plan-workflow`) is still one piece of work, and the
spec (`system-spec`) is still the map. Each can be used on its own or with the others.

`plan-workflow` takes one plan through the board. A **wave** coordinates many plans running at once
on separate tracks, each in its own session, under one **lead** session. The plans keep their own
objectives, phases and success criteria; the wave decides when each starts, who owns which files,
which contracts freeze when, and how the work comes back together.

The skill is generalised from two waves that ran: one of four tracks, and one of nine
tracks over two days with an integration window against real Azure resources.

**Invoked with arguments, this skill starts a role.** `lead` means read [`lead.md`](lead.md) and
follow it. `track <name>` means read [`track.md`](track.md) and follow it for that track. Either way,
read the rest of this file as the rules the role works under.

## Starting the sessions

Each session is supervised by a person, who approves its steps and resumes it after each pause point.
Name every session so it can be messaged: `wave-<n>-lead`, `wave-<n>-<track>`.

**Messages form a star with the lead at the centre.** A track messages the lead and nobody else; the
lead messages the tracks. Content one track needs from another goes through the lead, so the lead
always knows what changed hands and nothing bypasses a frozen contract.

| Where | Lead | A track |
|---|---|---|
| Terminal | `claude --agent ai-plugins-and-skills:wave-lead --name wave-<n>-lead` | `claude --agent ai-plugins-and-skills:wave-track --name wave-<n>-<track>`, then `track <track>` |
| Editor tab (VS Code extension) | New tab, `/ai-plugins-and-skills:wave-workflow lead`, then rename the session `wave-<n>-lead` | New tab, `/ai-plugins-and-skills:wave-workflow track <track>`, then rename it `wave-<n>-<track>` |

The agents (`wave-lead`, `wave-track`) only point to `lead.md` and `track.md`, so both routes give
the same instructions. Nothing is written per track: the track name finds the plan
(`wave-<n>_<track>_<date>`), and everything else comes from the waves context and the wave document.

## When to write a wave

When any of these is true:

- more than about four plans are live at once;
- a queued plan is held by something other than its stated dependency;
- two plans want the same track;
- the same file appears in three or more plans' `Files Expected to Change`.

Two or three plans need no wave; they need a look at `planning/in-progress/`. A wave written too
early is one more document to keep in sync.

## Layout

```
planning/waves/
├── waves-context.md                 # kind: context — every session reads it first
└── wave-<n>/
    └── wave-<n>_<YYYY-MM-DD>.md     # kind: wave — the wave document

planning/WAVE-README.md                    # generated sign; exists only while a wave is open
planning/wave-<n>_lead_<YYYY-MM-DD>.md     # the lead's plan, on the board like any plan
planning/wave-<n>_<track>_<YYYY-MM-DD>.md  # one per track
```

The plans live on the board, because their directory is their status; the `wave-<n>_` prefix keeps
them together there.

**`WAVE-README.md` is the sign that a wave is running.** `plan-doc.cs wave-table --fix` writes it at
the board's root while a wave document that carries the wave-table markers is `status: open`, and
deletes it when none is. It holds the start commands, the link to the waves context and the wave
document, and the plan table with each plan's current location. It is generated, so it cannot go
stale, and `validate` reports `WAVE-README-STALE` if it is missing, edited or left up. Outside a wave,
the root of `planning/` goes back to holding only what is queued. A wave folder may also hold supporting `kind: wave` documents, such as a list
of work only a person can do. Wave numbers are never reused, and a wave is never deleted.

Start from the templates beside this file:

| Template | Becomes |
|---|---|
| [`waves-context-template.md`](waves-context-template.md) | `planning/waves/waves-context.md` |
| [`wave-template.md`](wave-template.md) | `planning/waves/wave-<n>/wave-<n>_<date>.md` |

## The waves context file

`waves-context.md` is the one file every session reads first. Where it and `lead.md`, `track.md` or
a plan disagree, it wins. It is kept current while the wave runs. It holds:

- **the current wave number**, which tells every session which `wave-<n>_` plans are its own;
- **Repositories and skills:** where the code lives, the main branch, and the language skills
  tracks load;
- **How we work:** pushing, branching, merging and asking rules;
- **Where things stand:** what is deployed and merged, and facts a session cannot get from the files;
- **Rules learned:** each one a mistake that cost time, written as the rule that prevents it;
- **Status by track:** done, today's work, and the **pause point** where the track stops.

It outlives any one wave. When a wave closes, its status section is replaced by the next wave's,
and the rules learned stay.

## The wave document

| Section | Holds |
|---|---|
| Deliverable | What exists when the wave is done, in terms a user would see |
| Integration gate | Numbered checks, each runnable and observable. These are the exit criteria |
| Tracks and plans | Order, track, plan name, start day, and why that order |
| Plans on the board | Generated by `plan-doc.cs wave-table --fix` between `<!-- wave-table:begin -->` and `<!-- wave-table:end -->`: every plan in the wave (by `wave-<n>_` prefix or `wave:` field), its track, tier, repo and status, and a link to where it sits now. Regenerate after any plan moves; `validate` reports `WAVE-TABLE-STALE` until you do |
| Contracts | Each with an **owner**, its **consumers** and a **freeze day**, and enough shape (routes, types, tables) that consumers can code against it before it exists |
| Decisions from the plan review | The lead's answers to the open questions the plans left for it |
| Who owns which files | Every path a track may edit, with exceptions spelled out |
| Calendar | Relative days (D1, D3 …) for starts, freezes and the integration window |
| Integration window | The order of deployment, the work only people can do, and the order the gate runs in |
| Out of scope | What the wave does not do, with the stub that holds each item |
| Risks | Each with what keeps it small |

A wave **answers** the questions its plans deferred to the lead, and its answers win over the
plans. It does **not** re-plan: a plan's objective, phases and success criteria stay in the plan.
A wave that edits those has become a very large plan.

## Setting up a wave

1. Write the wave document from the template, a plan for the lead (`wave-<n>_lead_<date>`), and a
   plan per track. Name each plan `wave-<n>_<track>[-<name>]_<YYYY-MM-DD>.md` (`plan-workflow`), so
   the wave's plans sort together on the board and every reference to one says which wave it is in.
   Give each one `wave: wave-<n>`, `tier:`, `author:` and, where the code lives elsewhere, `repo:`
   in its frontmatter (`plan-writing-syntax`). Leave the order and start day to the wave document's
   Tracks and plans table: scheduling belongs to the wave. Put anything one track needs and the
   others don't, such as an extra skill, in that track's plan. Then run
   `plan-doc.cs wave-table --fix`.
2. **Promote every backlog stub a plan absorbs:**
   `plan-doc.cs close --doc <stub> --to completed --promoted-to <plan>`. An absorbed stub left in
   `backlog/` shows as open work. List the stubs per plan in the wave document's Tracks and plans
   table.
3. **File a stub for every item the wave defers,** and name it in Out of scope.
4. Answer the plans' open questions in the wave document.
5. Send each High-tier plan (`spec-tier`) through `plan-send-review`, and record the outcome with
   `plan-record-review`.

Refer to plans by name (`wave-1_store_2026-10-21`), never by path. A plan moves from the queue to
`in-progress/` to `completed/`, and a hand-written path goes stale on the first move. The generated
wave table is the one place a plan's current path appears, because it is regenerated.

## Admitting a plan to a track

A plan may start when all four hold, checked in this order:

1. **Its track is free.** No other in-progress plan declares the same track.
2. **Its file set is disjoint** from every in-progress plan's `Files Expected to Change`.
3. **Its stated dependencies are met**, or it codes against a frozen contract with a named
   stand-in.
4. **If it is High tier, its adversarial review is recorded** (`plan-record-review`). The rule is
   the same for the lead and the tracks, so a track cannot admit itself ahead of its review.

```powershell
dotnet run "$plan\plan-doc.cs" -- validate --path planning   # TRACK-COLLISION, FILESET-OVERLAP
```

A plan with no `Files Expected to Change` section cannot be checked against rule 2, so it does not
start until it has one.

## Files more than one track needs

`plan-workflow`'s rule 2 stays: file sets are disjoint. The wave is where a shared file is
resolved, in one of three ways, and the "Who owns which files" table records which:

| Way | When | How it went |
|---|---|---|
| **Name an owner** | One track's change, and it can be written now | Another track that needs content in the file writes it and sends it to the lead, who passes it to the owner to commit. In one wave, the runtime track wrote the SDK section of `HOW-TO-INTEGRATE.md` and the consumer track committed it |
| **Split the file** | Each track needs its own part, and none can be written yet | One fragment per track; the shared file changes once, in a gate. Prove the split mechanical: the test count is unchanged and no test file is edited |
| **Gate it** | The change touches other plans' file sets | A small plan on `main` that runs alone and finishes before the others start |

**The third claimant is the signal.** One shared file is a coincidence; three is a file doing two
jobs, and the wave should split it rather than schedule around it.

**Append-only registries are the exception.** A file every track adds one line to, such as a
solution file that each new project joins, may be exempted from ownership. The wave must say so by
name, and the lead resolves the additions in pull request order.

**A file no track owns,** such as a shared guide or an index, is owned by the lead. Tracks send the
text they need in it, and the lead edits the file in the integration window. Otherwise it either goes
stale or several tracks edit it at once.

**When one track's change breaks another track's test,** the fix goes into the same pull request as
the change. The typical case is a test that pins a stand-in the other track's real code replaces.

## Roles

### The lead

- Writes no production code.
- Keeps `waves-context.md` current, and tells the tracks to re-read it when it changes.
- Answers contract questions. A change to a frozen contract goes through the lead only.
- Receives a SHA from each track and verifies it from a clean checkout: a fresh worktree, no `bin/`
  or `obj/`, warnings as errors, every test project. Then pushes the branch and opens the pull
  request with the host's tooling (`gh pr create`, or your platform's equivalent).
- Before pushing a follow-up to an open pull request, confirms it is still active. A merged branch is
  spent: squash merges leave nothing to add to, so follow-up work goes on a new branch.
- Reports pull requests with full links, one per line, in the order to complete them.
- Sends work only a person can do to that person as a checklist.

### A track

- Works in its own git worktree on `track/wave-<n>-<track>`, so another track's RED never shows up as
  its own failure.
- Edits only the files its plan lists. If it needs another track's file, it stops and asks the lead.
- Stops at its pause point, reports, and waits for the lead.
- Never pushes and never merges. It sends the lead a SHA once it has verified from a clean checkout.
- Closes its plan through `plan-workflow` (Status section, `completed/` or `blocked/`).

## Contracts and freezes

Each contract has one owner and a freeze day. Before the freeze, consumers code against the shapes
in the wave document with the stand-in their plan names; the owner may still change them. After it,
the contract is merged and changes only through the lead. Freeze producer contracts first and start
their consumers once they are frozen (in one wave the consumer track started on D4, after the SDK API was
proposed, and the API froze on D5).

## The integration window

Run it as supervised sittings, not as one script:

- one plan for the window, with a sitting per resource owner (database, identity, CI/CD,
  configuration), then the gate;
- a read-only check before every write;
- secrets generated inside the command that uses them, never printed;
- a rollback table: for each write, how to undo it;
- sessions ask before an irreversible write, even in bypass mode.

People-only work, such as redirect URIs, role assignments, environment approvals and service
connections, goes to the person as a checklist before the window opens. Service connections are
created per project and never shared.

## Closing a wave

1. Run the integration gate and tick each item with its evidence.
2. **Close the lead's own documents.** Tracks close their plans; nobody else closes the integration
   window's plan or the people-only reminders. Give the plan a Status section, and close each
   reminder with what happened.
3. **Reconcile the spec and the project plan** with what shipped. Set each delivered feature to
   `Shipped` or `Shipped interim`, delete interim blocks whose target shipped, and run
   `spec-table --fix` (`system-spec`). Update the project plan's row for the wave.
4. Check that the waves context cites plans by name. Fix any path that went stale.
5. Set the wave document's `status: done`, then run `plan-doc.cs wave-table --fix`. That fixes the
   plan table as it closed and takes down `planning/WAVE-README.md`.
6. In `waves-context.md`, mark every track done, add the rules learned, and set the current wave to
   the next one.
7. File what the next wave inherits as backlog stubs.

## Do not

- **Do not re-plan inside a wave.** Answer the plans' open questions; leave their objectives alone.
- **Do not let a wave outlive its accuracy.** If it no longer describes the board, close it and
  write the next one.
- **Do not let a track push,** and do not reuse a merged branch.
- **Do not run two test-first tracks in one working tree.** RED is deliberate, and a shared tree
  makes one track's RED every track's failure.
