---
name: system-spec-derive
description: Use when an existing system has no system spec and no usable plans to build one from — writes a draft system spec by reading the code, docs, ADRs and tests, with every feature citing its evidence. For a system built plan by plan whose plans are in planning/, use system-spec-backfill instead. The spec format belongs to system-spec.
metadata:
  category: workflow-composers
---

# Derive a System Spec from an Existing System

This skill writes a system spec for an area that never had one and whose plans are missing,
scattered or unusable. The result describes what the system does today, in the `system-spec`
format, with `status: draft` until the spec's owner accepts it.

| The area has | Use |
|---|---|
| A system spec | `system-spec`, to edit it |
| Plans with feature-specs or design sections in `planning/` | `system-spec-backfill` |
| Code, and little else that records intent | this skill |

The terms (system spec, feature-spec, feature) are `system-spec`'s.

**This skill is read-only on the system.** It reads code and writes one spec file per area. It
changes no code, and it does not fix behaviour it finds surprising. It records what it found.

## Steps

### 1. Inventory the sources

List what exists, before reading any of it in depth:

| Source | Where to look | What it gives |
|---|---|---|
| Code structure | `code-review-graph` (`get_architecture_overview_tool`, `list_communities_tool`, `list_flows_tool`), or `codebase-mapper` | Modules, entry points, call flows |
| Public surface | Controllers, endpoints, message handlers, CLI verbs, OpenAPI files | What callers can observe |
| Tests | Test projects and their names | Behaviour someone cared enough to pin |
| ADRs | `documentation/ADR/`, `docs/adr/` | Why the system is shaped as it is |
| Docs | READMEs, wikis, runbooks | Intended behaviour, often out of date |
| Configuration | App Config keys, feature flags, schedules | Behaviour that varies by environment |

Record the inventory in the conversation, not in the spec.

If `code-review-graph` returns no communities or flows, build the graph first
(`build_or_update_graph_tool`). That writes only the graph's own cache, never the system's code.

### 2. Propose an area split and confirm it

One spec per area (`system-spec` rule 1). Propose the areas from the code's own boundaries:
communities in the graph, deployable units, or bounded contexts. Name each area and its spec.

- **Name:** the template's `<area>-<subject>-spec`, or `<area>-spec` when the area needs only one
  spec. The `name:` must equal the filename stem.
- **Location:** beside the code, in `docs/specs/`. That directory is what `--specs` points at in
  step 5.

**Stop and confirm the split, names and location with the user before drafting.** An area split is
expensive to change after plans start citing `<spec>#<slug>`.

### 3. Draft features from observed behaviour

For each area, copy `system-spec`'s `spec-template.md` and keep its layout: features are level-3
headings under a level-2 topic. Write one feature section per behaviour a caller can observe,
stated as observable state:

- what becomes true,
- what must stay true while it happens (the **frame**: the data and behaviour the feature never
  changes), and
- how anyone would know without reading the code.

Describe what the code does, not how. A spec that names the `WHERE` clause is the code written in
prose.

**Status.** A behaviour the default branch implements is `Shipped`. In `system-spec`, `Shipped`
means in production, and the code can't show that, so ask the user whether the default branch is
what's deployed, and note the answer under **Open questions** if nobody knows.

Don't write interim blocks. An interim block needs a change kind and a retire-when, which are
intent that code can't supply. Where a behaviour is partly built or hidden behind a flag, describe
what actually runs as the feature's text, and put the gap under **Open questions**.

**Where things go when the code doesn't settle them:**

| Finding | Goes in |
|---|---|
| A doc or ADR promises a behaviour, and the code doesn't implement it | **Not yet specified**, citing the doc. It is wanted and not yet written up |
| A doc and the code disagree about a behaviour the code does implement | The feature describes the code. The disagreement goes under **Open questions** |
| You suspect a behaviour but can point to nothing that shows it | Nowhere. Don't write it |
| The code does something that looks like a defect | The feature describes it as it is. The concern goes under **Open questions** |

**Open questions** is a level-2 section this skill adds at the end of the draft, after **Not yet
specified**. It lists each question for the owner, with the file and line that raised it. The owner
answers each one, and the section is deleted before the spec is accepted.

### 4. Cite the source of every feature

Under each feature line, add a source list:

```markdown
### Order cancellation
**Feature:** order-cancellation · **Status:** Shipped

- **Source:** `src/Orders/CancelOrderHandler.cs:42`, `tests/Orders.Tests/CancelOrderTests.cs`,
  `docs/adr/0004-order-cancellation.md`.
```

These are pointers into the code, not plan evidence (`planning/evidence/`). They stay after
acceptance, because they tell the next reader where to check that the text is still true.

### 5. Set the spec to draft

Frontmatter per `system-spec`, with `status: draft` and the `owner` the user names. The template
already has the status-table markers. Run `spec-table --fix`, then `validate --specs <dir>` with the
directory from step 2 (see `system-spec`). The owner reads the draft, answers the open questions,
and sets `status: accepted`.

### 6. Defer to `system-spec` for the format

The frontmatter, feature lines, status vocabulary, interim blocks and change kinds all belong to
`system-spec`. If this skill and `system-spec` disagree, `system-spec` is right.

## Do not

- **Do not change code** to make it match what you think the spec should say. Record the
  question.
- **Do not invent intent.** A behaviour with no evidence is not a feature yet.
- **Do not write one spec for the whole repository** when it has several areas. Confirm the split.
- **Do not set `status: accepted`.** Only the spec's owner accepts it.
