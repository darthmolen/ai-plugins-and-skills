---
name: plan-writing-syntax
description: Use when writing or editing any planning document — a plan, a backlog stub, a reminder, a review or a wave — to get its frontmatter right. Owns the shape of the document — the kind and status fields, the per-kind status vocabularies, what each status is required to carry, and cross-references written as names rather than paths. Pairs with plan-workflow, which owns which directory a document lives in and when it moves. Use it before writing the first line, not after a validator refuses the file.
metadata:
  category: workflow-composers
---

# The Shape Of A Planning Document

`plan-workflow` says **which column a card is in**. This says **what is written on it**.

Everything below is one contract: a small YAML block at the top of every document under
`planning/`, from a closed vocabulary, checked by `npm run validate:plans` in repositories that
have it.

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

## The block

```yaml
---
kind: plan            # plan | stub | reminder | review | wave
status: completed     # from kind's vocabulary, below
track: main           # plans only
date: 2026-09-04      # authored, or discovered for a stub
completed: 2026-09-05 # required by status: completed
---
```

**Prose stays in the body.** Frontmatter carries what a machine reads; the reasoning is what these
documents are *for*, and flattening it into YAML would be the tail wagging the dog. Write the
argument underneath, at whatever length it deserves.

`date` is ISO `YYYY-MM-DD`, always.

## `status`, by `kind`

Two vocabularies, because there genuinely are two. Forcing a reminder into the plan enum would be
a lie about what a reminder is.

| kind | status |
|---|---|
| `plan` | `queued` · `in-progress` · `completed` · `not-implemented` · `blocked` |
| `stub` | `open` · `promoted` · `closed` |
| `reminder` | `open` · `done` · `dropped` |
| `review`, `wave` | `open` · `done` |

## What a status must carry

A status that names nothing says nothing.

| status | requires |
|---|---|
| `completed` | `completed:` — the date, which is the thing `completed/` is claiming |
| `blocked` | `blocked_on:` — a blocker with no name is one nobody can clear |
| `promoted` | `promoted_to:` — and it must resolve |
| `not-implemented` | `reason:`, from four, each naming something |
| `closed` | `closed_by:` **or** `closed_reason:` — "closed" alone says nothing |

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

A name is the filename **minus its status prefix and `.md`**. Only `feature_`, `promoted_` and
`closed_` are stripped — those are the parts that change with status. A reminder's `verify_` or
`follow-up_` is a *category*, and stays part of the name.

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
| `closed_by` | a `plan` or a `stub` |
| `plan` (on a reminder) | a `plan` or a `stub` — a reminder outlives the promotion of whatever raised it |

**A reference resolving to zero or to more than one document is an error**, naming every
candidate. Globbing without that check would trade a stale-path bug for a silent wrong-target bug.

Two documents can share a name: a stub promoted the same day it was filed shares slug *and* date
with the plan it became. `kind` separates them, and so does the rule that **nothing references
itself** — a document is never promoted from, superseded by, or closed by itself.

## The directory and the prefix have to agree

The directory **is** the status, so a `completed` plan in the queue root is a document lying about
itself. Prefixes stay too, because `ls planning/backlog/` reading as a status board is a real
affordance frontmatter does not give back — and **redundancy that is checked is a second opinion,
not drift.**

| kind + status | directory | filename prefix |
|---|---|---|
| `plan/queued` | `planning/` | — |
| `plan/in-progress` | `planning/in-progress/` | — |
| `plan/completed` | `planning/completed/` | — |
| `plan/not-implemented` | `planning/not-implemented/` | — |
| `plan/blocked` | `planning/blocked/` | — |
| `stub/open` | `planning/backlog/` | `feature_` |
| `stub/promoted` | `planning/backlog/` | `promoted_` |
| `stub/closed` | `planning/backlog/` | `closed_` |
| `reminder/open` | `planning/reminders/` | — |
| `reminder/done`, `reminder/dropped` | `planning/reminders/completed/` **or** `planning/reminders/` | — |
| `wave` | `planning/waves/` | — |

**A closed reminder has two homes on purpose.** Closing and filing are two acts: a tool closes a
reminder in place, and a person files it afterwards. Demanding the move at close time would fail
the board the moment anyone used the button.

`README.md` files are signage, not documents of the corpus. They carry no frontmatter and are
skipped.

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

A stub that became a plan:

```yaml
---
kind: stub
status: promoted
date: 2026-08-30
promoted_to: seed-a-test-household_2026-08-31
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
- **Do not skip `track:` on a plan.** `in-progress/` holds one plan per track, and that is checked.

## Checking

```bash
npm run validate:plans        # from the workspace root that defines it
```

It reports the file, the rule, the problem and the remedy, and exits non-zero. Where a repository
has one, **the validator is the authority, not this page** — read its source if the two ever
disagree, and fix this page.
