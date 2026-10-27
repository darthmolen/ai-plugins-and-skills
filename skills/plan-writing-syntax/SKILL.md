---
name: plan-writing-syntax
description: Use when writing or editing any planning document — a plan, a backlog stub, a reminder, a review or a wave — to get its frontmatter right. Owns the shape of the document — the kind and status fields, the per-kind status vocabularies, what each status is required to carry, and cross-references written as names rather than paths. Pairs with plan-workflow, which owns which directory a document lives in and when it moves. Use it before writing the first line, not after a validator refuses the file.
metadata:
  category: workflow-composers
---

# The Shape Of A Planning Document

`plan-workflow` says **which column a card is in**. This says **what is written on it**.

Everything below is one contract: a small YAML block at the top of every document under
`planning/`, from a closed vocabulary. The contract is enforced by
`plan-workflow`'s `scripts/plan-doc.cs validate`, which is the authority where it and this page
disagree.

## Why a field and not a sentence

Status used to live in three places that could disagree — the **directory** a document sat in,
the **filename prefix**, and a **prose line** in the body. One repository measured it: 109
documents carrying `**Status:**` in **24 distinct shapes**. Five spellings of *finished*. Six of
*ended without being built*.

Three mechanisms, one fact, and they had already drifted: a promoted stub was deleted outright
leaving its plan pointing at a file that did not exist, and a stub invented a third filing
convention in its own body.

**The condition on all of this: frontmatter with no validator is markdown with more punctuation.**
The same 24 shapes reappear in YAML inside a month. The schema and the check ship together or
neither is worth doing.

The prose line went first. The **filename prefix** went second: it now names the document's *kind*,
where it is stable, rather than its status, where it renamed the file on every move. Status lives
in the directory, with the `status:` field as the checkable second opinion. A stub became a baby plan in the
same change: one lifecycle for both, with the prefix carrying the kind.

## The block

```yaml
---
kind: plan            # plan | stub | reminder | review | wave | context
status: completed     # from kind's vocabulary, below
track: main           # required in in-progress/, for a plan or a stub
date: 2026-09-04      # authored, or discovered for a stub
completed: 2026-09-05 # required by status: completed
decided_by: A Name    # optional; who made the call
decided_on: 2026-09-05 # optional; when
delivers: [pricing-engine-spec#rule-flows]  # optional; spec features this plan delivers
author: Claude Code with A Name  # optional; who wrote it
tier: high            # optional; low | medium | high, from spec-tier
repo: Example.Orders.Api  # optional; where the code it changes lives, when not this repo
wave: wave-1          # required on a wave-<n>_ plan and must match it; optional otherwise
---
```

**Prose stays in the body.** Frontmatter carries what a machine reads; the reasoning is what these
documents are *for*, and flattening it into YAML would be the tail wagging the dog. Write the
argument underneath, at whatever length it deserves.

`date` is ISO `YYYY-MM-DD`, always.

**`author:`, `tier:`, `repo:` and `wave:` replace bold lines at the top of the body,** so they can be
read at a glance and stay with the file when it is copied out of the repository. All four are
checked:
- `tier:` is `low`, `medium` or `high` (`BAD-TIER`).
- An `author:` or `repo:` that is present but empty is `EMPTY-FIELD`.
- `wave:` is `wave-<n>` (`BAD-WAVE`). A `wave-<n>_` plan must carry it (`MISSING-FIELD`), and the two
  must agree (`WAVE-MISMATCH`). The prefix is for a glance at the board, and the field is for the
  file on its own.
- On a plan without the prefix, `wave:` alone joins the plan to its wave, so an older wave can adopt
  the convention without renaming its plans. Either way, the wave must exist (`UNKNOWN-WAVE`).

## `status`, by `kind`

Two vocabularies, because there genuinely are two — but the split is **on the board or not**, not
per kind. Forcing a reminder into the board enum would be a lie about what a reminder is.

| kind | status |
|---|---|
| `plan`, `stub` | `backlog` · `queued` · `in-progress` · `completed` · `not-implemented` · `blocked` |
| `reminder` | `open` · `done` · `dropped` |
| `review`, `wave`, `context` | `open` · `done` |

**A stub is a baby plan**, so it
shares the plan's vocabulary and its directories. Nothing is illegal for a stub: if someone works
one directly it takes a track and ships. `kind:` says how big and how mature a document is, never
where it is in its life.

`backlog` means **deferred: nobody has committed to this yet** — as true of a plan that was worked
and then outranked as of an idea captured mid-session. It is not "where stubs live".

## What a status must carry

A status that names nothing says nothing. Keyed by status alone: a stub is required to carry no
more, and no less, than a plan of the same status.

| status | requires |
|---|---|
| `completed` | `completed:` — the date, which is the thing `completed/` is claiming |
| `blocked` | `blocked_on:` — a blocker with no name is one nobody can clear |
| `not-implemented` | `reason:`, from four, each naming something |
| `backlog`, `queued` | nothing — capture has to stay cheap or ideas go uncaptured |

**`promoted_to:` is deliberately not on that list.** Promotion *is* a stub's completion, and which
terminal bucket a stub lands in is judgement rather than something to police. Write it when you
know the plan's name; it is checked for shape and resolution only if present.

### `not-implemented` — the second terminal state

For a plan that **built nothing**. `completed/` is wrong: a reader counting it to see what the
system does would count a plan that did not. The queue root is worse — it means *ready to start*,
so a dead plan there is a permanent false positive on "what is next".

| reason | means | must name, via |
|---|---|---|
| `superseded` | a later plan covers this ground | `superseded_by:` |
| `folded-in` | absorbed into another plan, which shipped | `folded_into:` |
| `obsoleted` | the ground moved, or it was ruled out | `obsoleted_by:` (free text) |
| `abandoned` | tried, and the approach was rejected | `learned:` (free text) |

`abandoned` is the one that earns the directory. **A rejected approach is knowledge** — the next
person to have the same idea should find the document saying it was tried.

## References are names, never paths

**This is the rule most likely to be got wrong, and the one with the most behind it.**

```yaml
promoted_to: seed-a-test-household_2026-08-31      # yes
promoted_to: planning/completed/feature_seed-a-test-household_2026-08-31.md   # no
```

A name is the filename **minus its kind prefix and `.md`**. Only `feature_`, `bug_` and `stub_`
are stripped. A plan's `wave-<n>_` stays, so `wave-1_read_2026-10-26` says which wave it is in.
A reminder's `verify_` or `follow-up_` is a *category*, and stays part of the name.

The retired `promoted_` and `closed_` are **no longer stripped**, which is deliberate: a document
still carrying one keeps it in its name, so an un-migrated repository fails loudly rather than
resolving quietly against the wrong document.

Documents move constantly: `planning/` → `in-progress/` → `completed/`, and sometimes back, and a
stub outlives all of it. **A stored path is wrong the moment its target moves.** A name is not,
because the name is the identity and the directory is the status. This removes a failure mode
rather than detecting one: a promoted stub's pointer used to need rewriting when its plan
completed, and nothing ever asked for that second edit.

| field | resolves to |
|---|---|
| `promoted_to` | a `plan` |
| `promoted_from` | a `stub` **or** a `plan` — an item is promoted out of whatever held it, sometimes another plan's *Anticipated Backlog* section |
| `superseded_by`, `folded_into` | a `plan` |
| `plan` (on a reminder) | a `plan` or a `stub` — a reminder outlives the promotion of whatever raised it |
| `reviews` (on a review) | the `plan` or `stub` the review copy was made from |

**A reference resolving to zero or to more than one document is an error**, naming every
candidate. Globbing without that check would trade a stale-path bug for a silent wrong-target bug.

Two documents can share a name: a stub promoted the same day it was filed shares slug *and* date
with the plan it became. Their *filenames* differ (`stub_` versus `feature_`) but their **names**
collide, so `kind` is still what separates them — as is the rule that **nothing references
itself**: a document is never promoted from, superseded by, or folded into itself.

## The directory and the prefix have to agree

The directory **is** the status, so a `completed` plan in the queue root is a document lying about
itself. **Redundancy that is checked is a second opinion, not drift.**

| kind + status | directory |
|---|---|
| `plan/backlog`, `stub/backlog` | `planning/backlog/` |
| `plan/queued`, `stub/queued` | `planning/` |
| `plan/in-progress`, `stub/in-progress` | `planning/in-progress/` |
| `plan/completed`, `stub/completed` | `planning/completed/` |
| `plan/not-implemented`, `stub/not-implemented` | `planning/not-implemented/` |
| `plan/blocked`, `stub/blocked` | `planning/blocked/` |
| `reminder/open` | `planning/reminders/` |
| `reminder/done`, `reminder/dropped` | `planning/reminders/completed/` **or** `planning/reminders/` |
| `review/open` | `planning/needs-review/`, `needs-review/in-progress/` or `needs-review/reviewed/` — the queue's three stages |
| `review/done` | `planning/needs-review/completed/` |
| `wave` | `planning/waves/`, or one folder below it (`planning/waves/<wave>/`) |
| `context` | `planning/waves/` |

**A closed reminder has two homes on purpose.** Closing and filing are two acts: a tool closes a
reminder in place, and a person files it afterwards. Demanding the move at close time would fail
the board the moment anyone used the button.

### The prefix carries kind, and never changes

| prefix | kind |
|---|---|
| `feature_` | `plan`: new behaviour |
| `bug_` | `plan`: a fix |
| `wave-<n>_` | `plan`: a track's plan in wave *n*, as `wave-<n>_<track>[-<name>]_<date>.md`. The wave document (`wave-<n>_<date>.md`) carries it too |
| `stub_` | `stub` |

It used to carry *status* — `feature_` → `promoted_` → `closed_` — which renamed the file on every
move and is the whole reason the name rule above needed a special case. `kind:` was already doing
the job; now the prefix does it too, visibly, and **a document's filename is stable for its life.**

Two rules, and they are not symmetric:

- **A stub must carry `stub_`.** It shares every directory with plans now, so the prefix is the only
  thing telling the two apart at a glance — the job the directory used to do and no longer does.
- **A plan need not carry `feature_`, `bug_` or `wave-<n>_`.** A *missing* prefix is a lost
  affordance, not a lie.
  Documents predating the convention read perfectly well without it, and renaming a completed
  document to satisfy a new rule is exactly what *the name is the identity* forbids.

A prefix belonging to **another** kind is always an error, on any document. That is a lie about
what the document is, and lies are what the validator is for. A `wave-<n>_` plan also has to name
a wave that exists: `UNKNOWN-WAVE` when `waves/wave-<n>/` holds no `kind: wave` document.

## Recording a decision

Two optional fields, for the one fact `status` cannot carry:

```yaml
decided_by: Jane Doe
decided_on: 2026-09-16      # ISO, and checked
```

Never required, and there is **no `decision:` field**. `status: completed` + `promoted_to:` already
says "this became a plan" and `status: not-implemented` + `reason:` already says "this ended without
becoming one"; an enum restating those would be a fourth place for one fact to live and disagree.
"What is waiting on *me*?" is `status: blocked` + `blocked_on:` — a board that already exists, and
that `plan-sweep-blockers` and `plan-clear-blockers` already work.

`README.md` files are signage, not documents of the corpus. They carry no frontmatter and are
skipped. So is `planning/WAVE-README.md`, which `plan-doc.cs wave-table --fix` generates while a
wave is open and deletes when it closes (`wave-workflow`); `validate` reports `WAVE-README-STALE`
when it is missing, out of date, or left up after the wave. **So is everything under `planning/evidence/`** — those are artifacts a plan produced and
its Status block cites, not documents of the corpus, and some are not markdown at all. Neither
appears in the table above, and both are excluded from the `^kind:` check below.

## Citing spec features with `delivers:`

A plan that delivers features of a system spec names them (the format is `system-spec`'s):

```yaml
delivers: [pricing-engine-spec#rule-flows, pricing-engine-spec#rule-sets]
# or
delivers:
  - pricing-engine-spec#rule-flows
  - pricing-engine-spec#rule-sets
```

Each entry is `<spec name>#<feature slug>`. The spec name is the spec's `name:` field, and the slug
is declared on the spec's feature line. Both are names, so the names-not-paths rule holds: a path or
a heading anchor is `DELIVERS-SHAPE`. The field is optional; a plan that touches no spec has none.

`validate` always checks the shape. With `--specs <dir>` it also resolves each entry
(`DELIVERS-UNRESOLVED`), and fails a completed plan whose feature still carries a release status
(`DELIVERS-STILL-RELEASE`). The spec side of the contract (`kind: spec`, its frontmatter, feature
lines and the generated status table) belongs to the `system-spec` skill. Specs live beside the
code, not under `planning/`, so `kind: spec` is not a planning kind.

## Worked examples

A queued plan:

```yaml
---
kind: plan
status: queued
track: main
date: 2026-09-03
---
```

A deferred stub, in `planning/backlog/` as `stub_<name>_<date>.md`. This is the resting state, and
it carries nothing beyond the three fields — capture has to stay cheap or ideas go uncaptured:

```yaml
---
kind: stub
status: backlog
date: 2026-08-30
---
```

A stub that became a plan. It sits in `completed/`, because **promotion is what completing a stub
looks like** — its job was to hold the idea until someone committed to it:

```yaml
---
kind: stub
status: completed
date: 2026-08-30
completed: 2026-08-31
promoted_to: seed-a-test-household_2026-08-31
decided_by: Jane Doe
---
```

A plan that was worked and then outranked, in `planning/backlog/`. The status a plan used to have
no honest home for — the queue root means *ready to start*, so parking it there was a false
positive on "what is next":

```yaml
---
kind: plan
status: backlog
track: main
date: 2026-08-27
---
```

A plan that was tried and rejected:

```yaml
---
kind: plan
status: not-implemented
track: main
date: 2026-08-27
reason: abandoned
learned: the second queue could not be made atomic without a lock the runner cannot hold
---
```

A reminder:

```yaml
---
kind: reminder
status: open
category: verify
audience: dm
subject: session
date: 2026-09-01
plan: area-2-scribes-rite-and-sandbox_2026-08-28
---
```

## Do not

- **Do not write a path where a name belongs.** It is the single most common mistake and the
  reason half this contract exists.
- **Do not leave a `**Status:**` prose line beside the field.** Two copies of one fact is the
  disease being treated. Prose in the body should be the *reasoning*, not the metadata.
- **Do not invent a status.** If none of the vocabulary fits, that is worth raising — the enum
  gained `dropped`, and a reminder gained a second legal directory, because reality did not fit
  and the schema moved rather than the truth.
- **Do not rename a document to fix a reference.** The name is the identity; fix the reference.
- **Do not rename a document when it moves, either.** The prefix carries kind, and kind does not
  change on a move. A filename that changes is the drift this replaced.
- **Do not skip `track:` in `in-progress/`.** It holds one document per track, and that is checked —
  for a stub being worked directly exactly as for a plan.
- **Do not add a field that restates `status`.** Apply the discrimination test: if it only says
  again what the status and directory already say, it is a fourth place for one fact to disagree.

## Checking

```bash
# Every document under planning/ carries a kind (evidence/ is exempt, see above):
grep -rL "^kind:" planning/ --include=*.md | grep -v '/evidence/'

# Status and directory must agree — this lists what each column claims
grep -rH "^status:" planning/ --include=*.md | sort -t: -k3

# One document per track in in-progress/
grep -H "^track:" planning/in-progress/*.md

# What shipped, as opposed to what was merely promoted — completed/ holds both now
grep -l "^kind: plan" planning/completed/*.md | wc -l

# Nothing left carrying a retired status prefix
ls planning/**/promoted_* planning/**/closed_* 2>/dev/null
```

The greps are a quick eyeball. The real check reports the file, the rule and the problem, and
exits non-zero:

```powershell
dotnet run "$plan\plan-doc.cs" -- validate --path planning
```

It ships with `plan-workflow`. **The validator is the authority, not this page** — read its source
if the two ever disagree, and fix this page.
