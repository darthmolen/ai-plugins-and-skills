# Slide outline template

Derived from the approved presentation outline. A renderer turns each block into one slide.

```markdown
# <Talk title> — slide outline

| Slide | Segment | Minutes |
|---|---|---|
| 1 | Title | — |
| 2–4 | 1. <segment name> | <n> |
| 5–8 | 2. <segment name> | <n> |

## Slide 1 — <Title>

**Claim:** <the one sentence this slide makes true for the audience>
**On slide:** <the words that appear on the slide: a title and at most three short lines>
**Visual:** <diagram, screenshot, table or code block to show; "none" if text only>
**Notes:** <what the speaker says; never shown on the slide>
**Source:** `[S]` <file, heading> or `[P]`
```

## Rules for the blocks

- **One claim per slide.** If a slide needs two claims, it is two slides.
- **On slide** is short. Detail goes in **Notes**, and the handout carries what attendees need to keep.
- **Notes** are speaker notes. A renderer must not put them on the slide.
- A slide with a destructive or data-leaking step shows the pre-flight checklist before the step.
- Every slide names its outline segment in the table, and every segment has at least one slide.

## Worked example

```markdown
## Slide 4 — Both postures manage the wrong number

**Claim:** Spending without direction and refusing to spend fail the same way.
**On slide:** Spend without direction · Won't spend at all · Same mistake: watching the token count, not the goal
**Visual:** Two columns, one per posture, joined underneath by the shared mistake
**Notes:** Ask which posture the room recognises. Then read the quotation: "both make the same
mistake: managing token count instead of managing the goal."
**Source:** `[S]` cost-and-direction.md, "Two common postures toward token spend"; `[P]` the question to the room
```
