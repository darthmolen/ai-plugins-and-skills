---
name: system-spec
description: Use when writing or editing a system spec — the document under docs/specs/ that describes the target system for an area — or when scoping a release out of one. Owns the spec format — frontmatter, one evergreen spec per area that is never trimmed or copied per release, a feature line and status on each feature section, interim blocks for reduced deliveries, change kinds and the subset rule, and a status table generated from the sections and the plans that deliver each feature. Pairs with plan-workflow, whose plans cite features in delivers:.
metadata:
  category: workflow-composers
---

# System Spec

**The spec is the map of the product.** Plans (`plan-workflow`) deliver its features, and waves
(`wave-workflow`) run plans in parallel. Each can be used on its own or with the others.

A system spec describes the **target** system for one area at full detail. Plans deliver it a
piece at a time. The spec records what the system is and what has shipped; each plan records how
and why for one delivery. Each rule below carries
the reason for it.

Start a new spec from [`spec-template.md`](spec-template.md).

## Rules

1. **One spec per area, never copied per release.** No `-v1` or `-mvp` copy. Git history holds
   earlier versions.
2. **The target text is never trimmed to fit a release.** What a release leaves out is recorded as
   a status and, where a reduced form ships, an interim block. Non-goals hold only permanent
   exclusions; "not in v1" is a status.
3. **A feature is a section with a feature line.** Headings without one are ordinary prose.
4. **The status table is generated.** Edit the feature lines and interim blocks; never edit the
   table.

## Frontmatter

```yaml
---
kind: spec
name: pricing-engine-spec    # must equal the filename stem; plans cite it
area: pricing
owner: Jane Doe              # approves any Migration interim
status: draft                # draft | accepted | retired
---
```

The document `status` says whether the spec itself is agreed. Feature status, below, says whether
each feature has shipped.

Specs are found by `kind: spec`, so they can live under `docs/` or `documentation/`.

## Feature sections

A feature section opens with its feature line, directly under the heading:

```markdown
## Rule flows
**Feature:** rule-flows · **Status:** v1 interim
```

- The slug is lower-case kebab (`rule-flows`) and unique within the spec. It is the feature's
  identity: a plan cites `pricing-engine-spec#rule-flows`, so renaming the heading breaks nothing.
  Changing a slug means updating every plan that cites it.
- The section runs to the next heading at the same level or higher. Subsections belong to it.
- Features stay under their topic. Shipping a feature changes one word in its feature line and
  moves no text.

### Status vocabulary

| Status | Means |
|---|---|
| `Shipped` | In production as specified |
| `Shipped interim` | In production in the form the interim block describes |
| `<release>`, such as `v1` | Committed to that release, as specified |
| `<release> interim` | Committed to that release, in the interim form |
| `Next up` | Specified and next in line; no plan yet |
| `Later` | Specified, not scheduled |

Wanted but not yet written up goes in a **Not yet specified** list, not in the table.

## Interim blocks

Where a delivery provides less than the target, keep the full target text and add an interim block
inside the section, as a heading that starts with `Interim`:

```markdown
#### Interim (v1)

- **Delivers:** a term picker plus a typed test box; compound conditions as JSON.
- **Omits:** operator filtering, plain-English rendering, the grouping UI.
- **Change kind:** UI only.
- **Retire when:** the guided builder ships. The term picker carries over.
```

The four fields are fixed. **Delivers** and **Change kind** appear in the status table. A status
ending in `interim` requires the block. When the target ships, delete the block and set the status
to `Shipped`; a block left on a non-interim feature fails validation.

### Change kinds and the subset rule

| Change kind | Means | Cost to replace |
|---|---|---|
| `UI only` | Stored data and API shapes are identical to the target | Replace at any time |
| `Contract subset` | Grammar, stored data or API is narrower than the target, and everything valid in the interim stays valid, with the same meaning, under the target | None; the target adds to it |
| `Migration` | The interim contract is not a subset of the target | The block must name what is converted, by what, and when |

**The subset rule:** anything valid in an interim release stays valid, with the same meaning,
under the target. A delivery that cannot meet it is a `Migration`. The migration is specified in
the plan before the plan is approved, and the spec's `owner` approves it.

## The status table

Put the markers near the top, after "How to read this spec":

```markdown
<!-- status-table:begin -->
<!-- status-table:end -->
```

`plan-doc spec-table --fix` fills them from the sections and from the plans:

| Column | Comes from |
|---|---|
| Feature, Section | the feature line's slug and its heading |
| Status | the feature line |
| Delivered as | the interim block's **Delivers**; `Target` for a scheduled or shipped feature; blank for `Next up` and `Later` |
| Change kind | the interim block's **Change kind** |
| Plan | every plan whose `delivers:` names the feature |

The script ships with `plan-workflow`, because resolving `delivers:` reads plans and specs with one
parser.

```powershell
$plan = if ($env:CLAUDE_PLUGIN_ROOT) {
  "$env:CLAUDE_PLUGIN_ROOT\skills\plan-workflow\scripts"
} else {
  "$HOME\.claude\skills\plan-workflow\scripts"
}

dotnet run "$plan\plan-doc.cs" -- spec-table --specs docs --path planning --fix
dotnet run "$plan\plan-doc.cs" -- spec-table --specs docs --path planning --check   # exit 1 if stale
dotnet run "$plan\plan-doc.cs" -- validate --path planning --specs docs              # the whole contract
```

`validate --specs` reports:

| Rule | Problem |
|---|---|
| `SPEC-MISSING-FIELD`, `SPEC-BAD-STATUS`, `SPEC-NAME-MISMATCH`, `SPEC-DUPLICATE-NAME` | frontmatter |
| `SPEC-BAD-FEATURE`, `SPEC-DUPLICATE-FEATURE`, `SPEC-BAD-FEATURE-STATUS` | feature lines |
| `SPEC-NO-INTERIM`, `SPEC-STALE-INTERIM` | an interim status without a block, or a block without an interim status |
| `SPEC-NO-TABLE`, `SPEC-TABLE-STALE` | the generated table is missing or out of date |
| `DELIVERS-UNRESOLVED` | a plan's `delivers:` names no spec, or no feature in it |
| `DELIVERS-STILL-RELEASE` | a completed plan delivers a feature still marked with a release status |

Code fences are ignored, so pseudocode with `#` comments and example feature lines is safe.

## Carving a delivery out of a spec

1. Pick the features. For each one delivered in reduced form, write the interim block and choose
   its change kind. A `Migration` needs the migration specified in the plan and the spec `owner`'s
   approval.
2. Set each feature's status to the release (`v1`, `v1 interim`).
3. Write the plan with `delivers:` naming each feature, then run `spec-table --fix`. The plan
   appears in the Plan column.
4. A feature spec embedded in the plan is checked against the system spec before the plan is
   approved: where it fits, and whether it conflicts.
5. When the plan completes, set each feature to `Shipped` or `Shipped interim`, and run
   `spec-table --fix`. Validation fails while a completed plan still delivers a feature with a
   release status.
