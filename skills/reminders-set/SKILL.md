---
name: reminders-set
description: Records a task a person must actively do, which no test or gate can catch. Use when a plan names work that needs a human, a machine, a conversation or a calendar — a check on hardware nobody on the team owns, a decision only one role can take, something to raise at the next session — and the plan alone would leave it to memory. Sits beside plan-workflow; it never schedules work, it remembers the work software cannot verify.
metadata:
  category: workflow-composers
  level: beside plan-workflow
---

# Set Reminders

A plan tracks work the repository can do. **A reminder tracks work a person must do**, where
nothing in the suite will ever notice it was skipped.

The two are different in a way that matters. A criterion like *"typecheck is clean"* fails
loudly the moment it is untrue. A criterion like *"every screen is legible on the low-end laptop
at 1366×768"* is silent forever — the plan can be complete, the tests green, the build clean, and
the criterion simply never met by anyone. It is not a gate. It is a task, and tasks that live
only in a plan's prose get finished by accident or not at all.

Reminders live in `planning/reminders/`. They are in source control like everything else.

**`plan-workflow` owns the directory and the move; `plan-writing-syntax` owns the frontmatter.**
This skill owns the judgement — when something deserves a reminder rather than a test, a plan
phase or a backlog stub, and what has to be written in the body for the reminder to be actionable
later.

**It is `reminders-set`, not `plan-reminder-set`, deliberately.** A reminder can be about
anything; its document merely happens to live on the kanban. `plan-writing-syntax` gives it its
own status vocabulary rather than folding it into the plan enum — *forcing a reminder into the
plan enum would be a lie about what a reminder is* — and its `plan:` reference is a name rather
than a path precisely because a reminder outlives whatever raised it. So this skill sits
*beside* the `plan-*` family rather than in it, and `metadata.category` is what files it with
them.

## When to write one

Write one when a plan names something that:

- **needs a machine nobody has to hand** — a specific device, a phone, a printer
- **needs a person** — a decision only one role can take, a conversation, a sign-off
- **needs an occasion** — the next session, the start of a work area, a scheduled window
- **cannot fail a test** — anything where "we forgot" and "we did it and it was fine" look
  identical afterwards

Do **not** write one for work a plan already owns. A plan phase is not a reminder; a backlog
item is not a reminder. If a test could catch it, write the test instead — that is always the
better answer, and this directory is for the cases where it genuinely is not available.

## The file

`planning/reminders/<category>_<slug>_<YYYY-MM-DD>.md`

The category prefix stays part of the document's name. `plan-writing-syntax` strips only
`feature_`, `promoted_` and `closed_` when resolving a reference, because those are the parts
that change with status — a reminder's `verify_` or `follow-up_` does not.

```markdown
---
kind: reminder
status: open
category: verify
audience: <role>
subject: <subject>
date: YYYY-MM-DD
plan: <plan-name>
---

# <What the person has to do, as an instruction>

## What to do

The instruction, concretely enough to act on without reading the plan.

## Why it cannot be a test

The specific reason software will never catch this. If this section is hard to write, the
reminder probably should have been a test.

## What it changes

What happens with each outcome — including the outcome where everything is fine, which is
the one most likely to go unrecorded.
```

**The metadata is frontmatter, not a prose block.** A `**Status:**` line in the body beside a
`status:` field is two copies of one fact, and the second one is the one that goes stale. See
`plan-writing-syntax` for the closed vocabularies — a reminder's `status` is `open`, `done` or
`dropped`, and nothing else.

## `plan:` is a name, never a path

```yaml
plan: feature-flag-rollout_2026-08-27                                   # yes
plan: planning/in-progress/feature_feature-flag-rollout_2026-08-27.md   # no
```

This is the rule most likely to be got wrong here, and a reminder is where it bites hardest.

A plan moves — `planning/` to `in-progress/` to `completed/`, and sometimes back to the queue.
**A reminder outlives all of that**, and often the whole point of it is that it is still open
after the plan has been marked complete. A stored path is wrong within days and rots into a dead
link exactly when someone finally goes looking.

The name is the identity and the directory is the status, so a name goes on resolving after every
move. `plan-writing-syntax` owns this rule in full, including what a reference must resolve to.

## The categories

| Category | What it means | Closes when |
|---|---|---|
| `follow-up` | A thing to go and do, at the first opportunity | it is done |
| `decision` | Something a person must choose, which code cannot infer | it is chosen and written down |
| `occasion` | Tied to an event rather than a date — a session, a window, the start of an area | that occasion passes |
| `verify` | Something believed true that nobody has actually observed | somebody looks |

`verify` and `follow-up` are easy to confuse. The test: if the expected answer is *"yes, fine"*
and you would be surprised otherwise, it is `verify`. If there is real work either way, it is
`follow-up`.

## Audience

**Roles, never people** — whatever role vocabulary the repository already keeps. One person
routinely holds two of them, so `audience` says which hat is being worn rather than who is in the
room, and it stays correct when the person changes.

It is load-bearing for one reason: **different roles have different time.** A reminder addressed
to a role that only exists during a live session has to be done in that session, competing with
whatever the session was for. One addressed to a role that works outside it does not. Getting
that wrong spends the scarce hour on setup instead of the work it was cleared for.

## Subject

A short, stable noun from the repository's own vocabulary — the kind of thing you would file it
under to find it again.

Keep the list short and reuse existing values before inventing one. Three subjects with four
reminders each is a directory somebody can read; twelve subjects with one each is a list of
filenames.

## Closing one

Set `status: done` and add a `closed:` field carrying the date and what happened:

```yaml
status: done
closed: 2026-09-06 — pushed over the LAN from the other machine; the key was never installed
```

The date is the day it was answered, not the day it was raised. The clause after the dash is the
whole point of keeping the file: **what actually happened.** "Done" on its own records that
somebody ticked a box, which is the one fact nobody will ever need.

A reminder that turned out not to matter is `dropped`, with the reason in the same field.

Change nothing else in the body. Do not reflow the prose, do not reorder the sections, do not
delete the ones that are now historical — the reminder is a record of a question and its answer,
and the question has to survive for the answer to mean anything.

**Never delete the file.** A reminder that was raised and answered is the record that it was
answered.

### Closing and filing are two acts

A closed reminder may sit in `planning/reminders/` **or** `planning/reminders/completed/`, and
`plan-writing-syntax` allows both on purpose. A tool closes a reminder in place; a person files
it afterwards. Demanding the move at close time would fail the board the moment anyone used the
button, so closing is never blocked on filing.

Where the repository has tooling that ticks reminders off — the **Reminders** VS Code extension
([`vscode-extension-plan-reminders`](https://github.com/darthmolen/vscode-extension-plan-reminders))
is one — point its close-label setting at whatever field this contract uses, so that what the
button writes and what the validator reads are the same thing. Machines read these files as well
as people, which is why the label is fixed rather than free prose.

## Checking the board

```bash
grep -l "^status: open" planning/reminders/*.md        # what is outstanding
grep -H "^audience:" planning/reminders/*.md           # what needs whom
grep -rl "^plan: <plan-name>$" planning/reminders/     # what a given plan left behind
npm run validate:plans                                 # where the repository defines it
```

Where a validator exists it is the authority, not this page.
