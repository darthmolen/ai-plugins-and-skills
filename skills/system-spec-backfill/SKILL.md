---
name: system-spec-backfill
description: Use when an area was built plan by plan and its plans in planning/completed/ and planning/in-progress/ hold the behaviour that a system spec should — merges each plan's feature-spec, or an older plan's design section, into a new or existing system spec in date order under the agree/amend/add rule, citing the plan each feature came from. For a system with no usable plans, use system-spec-derive instead. The spec format belongs to system-spec.
metadata:
  category: workflow-composers
---

# Backfill a System Spec from Existing Plans

This skill turns the plans an area was built from into the area's system spec. Each plan's
feature-spec, or the design section of a plan older than the term, merges into the spec in the
order the plans were completed. The result is in the `system-spec` format, with `status: draft`
until the spec's owner accepts it.

| The area has | Use |
|---|---|
| A system spec, and a plan completing now | `plan-workflow`'s completion step merges it |
| Plans with feature-specs or design sections in `planning/`, and no spec or a stale one | this skill |
| Code, and no usable plans | `system-spec-derive` |

The merge rule (agree, amend, add) is `plan-writing-syntax`'s, in *The `## Feature-spec` section*,
and the spec format and its terms are `system-spec`'s.

**Completed plans are never edited.** They are history. This skill reads them and writes only the
system spec, plus `delivers:` on plans still in flight.

## Steps

### 1. Find the plans that state the area's behaviour

Search `planning/completed/` and `planning/in-progress/` for `kind: plan` documents that state what
the area's system does:

- a `## Feature-spec` section (newer plans), or
- in older plans, a section that describes behaviour: Design, Approach, Decisions, Contract,
  Behaviour, API, Success Criteria, and the Status block's Outcomes.

The test is whether the plan says what the system does, not what kind of work it was. A
documentation plan that states how the area behaves counts. A plan that only moved, renamed or
reformatted things does not.

Skip stubs, and plans in `not-implemented/`, which built nothing. List each skipped plan and the
reason in the conversation, so the user can correct the list.

### 2. Choose the spec: name, location and areas

If the area already has a system spec, merge into it. Otherwise, copy `system-spec`'s
`spec-template.md` and keep its layout: features are level-3 headings under a level-2 topic.

- **Name:** the template's `<area>-<subject>-spec`, or `<area>-spec` when the area needs only one
  spec. The `name:` must equal the filename stem.
- **Location:** beside the code, in `docs/specs/`. That directory is what `--specs` points at in
  step 6.
- **Features:** assign each behaviour block to a feature slug. Use the existing spec's slugs where it
  has them.

**Confirm the areas, names and slugs with the user before merging.** Plans will cite these slugs in
`delivers:`.

### 3. Merge in date order

Sort the blocks by the plan's `completed:` date, then `date:` for plans still in flight. Apply each
block to the spec under the merge rule:

| The block | Merge |
|---|---|
| Matches the spec's current text for the feature | `agree`: no change |
| Changes the feature, and says how | `amend`: replace the text, and keep the why in the source list |
| Describes a feature the spec does not have yet | `add`: new feature section. In a new spec, every feature's first block is an `add` |

An older design section has no merge kind written on it. Infer one, and mark it `inferred` in the
source list.

**When the plans don't settle the text:**

| Situation | What to do |
|---|---|
| A plan records work done but no behaviour ("10 skill directories copied verbatim") | Read the shipped code or skill for that feature, and describe what it does, as `system-spec-derive` does. Cite the file in the source list as well as the plan |
| A plan changed a feature and doesn't say how | Describe the feature from the current code, and add the plan to **Open questions** |
| A later plan contradicts an earlier one, and neither says why | The later plan wins the text, because it shipped last. Add both plans to **Open questions** |
| A plan contradicts itself | Prefer its Status block's Outcomes, which record what happened, over its earlier sections. Add the plan to **Open questions** |
| A plan in flight contradicts a completed one | Don't merge it, because it hasn't shipped. Add it to **Open questions** |

**Open questions** is a level-2 section this skill adds at the end of the draft, after **Not yet
specified**. It lists each question for the owner and the plans that raised it. The owner answers
each one, and the section is deleted before the spec is accepted.

**Status** comes from the plan's column:

- A feature from a completed plan is `Shipped`.
- Where the plan delivered a reduced form, the status is `Shipped interim`, and the feature needs
  an interim block (`system-spec`). Fill the block from what the plan says it omitted. If the plan
  doesn't give a change kind or a retire-when, add the question to **Open questions**.
- A feature that only plans in flight touch gets the release they target, or `Next up`.

### 4. Add `delivers:` to plans in flight only

For each plan in `planning/in-progress/` or the queue root that delivers a feature in the new spec,
add `delivers: [<spec>#<slug>]` to its frontmatter. Do not touch completed plans.

A backfilled spec's generated status table therefore shows an empty Plan column for every shipped
feature. That's expected. The link to the completed plans is the source list in step 5.

### 5. Cite the plans each feature came from

Under each feature line:

```markdown
### Rule flows
**Feature:** rule-flows · **Status:** Shipped

- **From plans:** `pricing-rule-flows_2026-08-14` (add), `pricing-flow-branching_2026-10-02` (amend:
  branches on rule-set results), `phase_skill-import_2026-04-29` (add, inferred).
```

Plans are named, never linked by path. A name is the filename minus `.md` and minus only the
`feature_`, `bug_` or `stub_` prefix, so `phase_skill-import_2026-04-29.md` is named
`phase_skill-import_2026-04-29`, and an undated legacy plan keeps its whole stem
(`plan-writing-syntax`).

### 6. Set the spec to draft and validate

Frontmatter per `system-spec`, with `status: draft`. For `owner`, use the person the user names. If
nobody can be asked, use the `decided_by:` of the latest plan used, and add "confirm the owner" to
**Open questions**.

The template already has the status-table markers. Run `spec-table --fix`, then
`validate --path planning --specs <dir>` with the directory from step 2 (see `system-spec`). Fix
every `DELIVERS-UNRESOLVED` before handing the draft to the owner.

## Do not

- **Do not edit a completed plan**, even to add `delivers:`. Rewriting history falsifies it.
- **Do not resolve a contradiction silently.** The later plan wins the text. The owner still sees
  the conflict.
- **Do not merge a plan in `not-implemented/`.** It built nothing.
- **Do not set `status: accepted`.** Only the spec's owner accepts it.
- **Do not restate the format.** It belongs to `system-spec`, and that skill is right where the two
  disagree.
