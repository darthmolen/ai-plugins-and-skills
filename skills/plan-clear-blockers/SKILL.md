---
name: plan-clear-blockers
description: Turns planning/blocked/ into an agenda for a session that could actually clear it. Use when something is briefly available — a device, an environment, an hour with the one person who can decide, a maintenance window — and the question is "what do I do with it?". Groups by blocker rather than by plan, orders the work inside each group, cites where each step is already specified, and names what each one unblocks.
metadata:
  category: workflow-composers
  level: beside plan-workflow
---

# Clear Blockers

`plan-sweep-blockers` answers *what is stuck and why*. This one answers a different question,
asked at a different moment:

> **The thing is available for an hour. What do I do with it?**

Where *the thing* is whatever was scarce — a device nobody normally has, a staging environment,
the one person who can approve, a window when the system can be taken down.

A board cannot answer that. It is organised by plan, and **a plan is not a unit of anybody's
afternoon.** The four things worth doing while that resource is open usually live in four
different plans, and nothing in `planning/blocked/` puts them next to each other.

## The instructions already exist. Do not write them again.

This is the design constraint, and getting it wrong produces something that looks more useful
than it is.

The blocked plans already specify their own steps — that is what a plan is. Restating them here
creates a second copy that drifts from the one that owns it, and the drift is silent: the plan
gets revised, the agenda does not, and somebody follows the stale one.

**Cite; never restate.** The output points at the authoritative steps and adds only what is
written nowhere else: the grouping, the order, and what each one buys.

The value is **aggregation, prioritisation, dedup and ordering.** Not a summary of the plans — a
summary is what you get when you skip all four.

## Step 1 — read both halves

`planning/blocked/`, for the `blocked_on:` field on each plan. And `planning/reminders/`, for the
open ones. **Neither is sufficient alone:** the plan says what is stuck, the reminder says who or
what can move it and when, and the batching only appears when both are on the table.

Include `planning/in-progress/` too. A partially blocked plan stays in progress by design — see
`plan-sweep-blockers` — and its gated step belongs in this agenda even though the plan is not in
the blocked column.

## Step 2 — group by blocker, never by category

**Categories group for reading. Blockers group for doing.**

A reminder's `category` and `subject` are right for browsing a directory and wrong here. Two
reminders in one subject may need two different resources on two different days, and one in
another subject may need the same resource as one of them.

Group by **the thing that is briefly available**:

```
<the scarce resource>, one sitting
  · <task>   → unblocks <plan-name>
  · <task>   → unblocks <plan-name>
  · <task>   → unblocks <plan-name>
```

**That grouping is the output.** Three blocked plans and one shared blocker is invisible from any
single plan and obvious the moment they are stacked — and it converts three stalled tracks into
one afternoon.

## Step 3 — order inside the group

Ordering is judgement. These are the questions that decide it:

- **What does the rest depend on?** If one step is how the work reaches the resource at all, it
  goes first regardless of how interesting it is.
- **What is cheapest to discover is impossible?** A step that might reveal the resource cannot do
  the thing belongs early, while there is still time to react.
- **Who has to be present, and for what?** Check each reminder's `audience`. Work that needs only
  the operator can be done before the others arrive; work that needs somebody whose time is the
  scarce thing should not be spent on setup. Getting this wrong turns their hour into your
  preparation.
- **What shares setup?** Two steps needing the same environment open should be adjacent.

State the reason for the order in one line. An agenda whose sequence is unexplained gets
reordered by whoever runs it — which is fine, but then it should be a list, not an order.

## Step 4 — cite, and name the payoff

Each item carries three things and nothing else:

| | |
|---|---|
| **What to do** | one line, in the imperative |
| **Where it is specified** | the plan and phase, or the reminder — **by name**, not by paraphrase |
| **What it unblocks** | the plan that leaves `blocked/` when this is done |

Cite by document name, the way `plan-writing-syntax` requires of every cross-reference: these
documents move while the agenda is still on somebody's screen, and a path written this morning is
wrong by the time it is used.

The third column is what makes an agenda persuasive rather than a chore list. "Take the
measurement" is a task; "take the measurement → this plan stops being blocked" is a reason.

## Step 5 — ask, but only when asking pays

Offer to go deeper on one group **only when the list is long enough that reading it is work.**

At half a dozen open reminders the whole agenda fits on a screen, and a clarifying question costs
more than it saves — produce the agenda and let the reader choose. Past roughly fifteen, or
across more than three or four distinct blockers, ask which group is live today and detail that
one.

The question, when it is worth asking, is **which resource you have** — not which category you
prefer. "Do you have the device, or an hour with the approver?" is answerable in a word and
selects the group. "Which subject do you want detail on?" makes the reader do the grouping this
skill exists to do for them.

## Step 6 — the leftovers matter

Two things belong at the end of every agenda:

**Blockers nobody can clear by acting.** A plan waiting on another plan, or on a release that has
not happened, is a dependency rather than a task — `plan-sweep-blockers` deliberately files no
reminder for it. It is on nobody's afternoon. List these separately, with what would move them,
so the reader knows they were considered rather than missed.

**Decisions.** A blocker waiting on an *answer* rather than a *thing* needs no resource except
attention — so it can be cleared in a queue, in any order, anywhere. Reminders with
`category: decision` are the shortest path to unsticking a board, because nothing has to be
carried anywhere and nobody has to be anywhere.

## What this skill does not do

- **Change any plan or reminder.** It reads and orders. Closing a reminder afterwards belongs to
  `reminders-set`; moving a plan out of `blocked/` belongs to `plan-workflow`.
- **Answer a decision.** It surfaces the question and says what it is holding up.
- **Find blockers.** It reads what `plan-sweep-blockers` recorded. If the board is stale, sweep
  first — an agenda built on a stale board sends somebody to redo work that was finished last
  week.
