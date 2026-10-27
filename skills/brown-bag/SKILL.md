---
name: brown-bag
description: Use when someone wants to give a talk, brown bag, lunch-and-learn or internal presentation from existing documentation — paths, links or files uploaded into the conversation. Interviews them about audience and goal (or continues from ideas already in the prompt), drafts a presentation outline, then derives a slide-deck outline and participant material from it, iterates until they approve, and hands the Markdown to the harness's PowerPoint and Word capability. Triggers on "brown bag", "lunch and learn", "turn these docs into a talk", "I need slides for", "presentation from this documentation".
metadata:
  category: workflow-composers
---

# Brown Bag

Turns documentation into a talk: a presentation outline, a slide-deck outline and participant
material, all in Markdown, then PowerPoint and Word. The user is interviewed first, approves the
outline before anything is derived from it, and iterates until they are satisfied.

**REQUIRED SUB-SKILL:** load `technical-writing` before drafting. Its rules govern every sentence
in all three artifacts. If this file already contains a "Technical Writing" section (the portable
build), use that section instead. The portable build also includes the three templates below, under
their own headings, in place of the `references/` links.

The skill works in any harness: Claude Code, claude.ai, Copilot, ChatGPT. It needs no shell and runs
no scripts. Where a step says "write a file", write it to disk if the harness has a file system;
otherwise put it in the reply as a fenced Markdown block with the file name above it.

## The rules that keep the talk true to its sources

These come from a curriculum pipeline (the opt-in `curriculum` plugin), which builds courses from documentation. Each one
exists because its absence produced a wrong course.

1. **Sourced or proposed, and labelled.** Mark each point taken from a supplied document `[S]` and
   give its location (file and heading). Mark each point the presenter adds `[P]`. Never present a
   `[P]` point as `[S]`.
2. **Quotations are verbatim.** A quotation marked `[S]` matches the source character for
   character. Mark a cut with `...`. Before every hand-over, re-read each quotation against its
   source. Eight quotations once shipped trimmed or re-punctuated while presented as exact, and every
   one survived a read-through, so check them against the source, not from memory.
3. **Report gaps; do not fill them.** When the talk needs a step the documents do not give, add it to
   the outline's gaps list and tell the presenter. An invented procedure is wrong the moment the real
   one is written.
4. **Danger first.** Any step that can destroy work or leak data goes in a checklist before the
   steps it applies to, on the slide and in the handout.
5. **Organise by what the audience will do,** not by what the tool can do. Show the prompt to type or
   the command to run. Define each term once. Tell the reader where optional depth starts.
6. **Emphasis is chosen.** The interview records what the talk takes from each document. A document
   cited without that choice turns into a summary of itself.

## Step 1 — Intake

Read every path, link and upload the user gave. Then report, in one short list:

- each source read, with its headings or a one-line description;
- each source that could not be read, and why;
- anything in the prompt that already answers an interview question (topic, audience, length, angle).

Do not summarise the documents back at length. The user wrote or chose them.

## Step 2 — Interview

Ask one question at a time. Skip any question the prompt or an earlier answer already settled, and
say what you took from it. Offer a likely answer where the sources suggest one.

1. Who is the audience, and what do they already know?
2. What should they be able to do after the talk that they cannot do now?
3. How long is the slot: 15, 30 or 45 minutes, or something else? Is there time for questions?
4. Which documents, and which part of each, carry the talk?
5. Is there a demo or a live walkthrough?
6. What should be left out?
7. What does the presenter want to say that is not in the documents? (These become `[P]` points.)
8. Is there a slide template, brand or colour scheme to use?

End with a **talk brief**: one paragraph covering audience, goal, length, sources and their emphasis,
demo, exclusions and template. Ask the user to confirm or correct it. Do not draft until they confirm.

## Step 3 — Presentation outline

Write `presentation-outline.md` from the template in
[`references/presentation-outline.md`](references/presentation-outline.md). It is the speaker's plan:
segments with timings, the points to make in each, demo steps, questions to expect, sources, and the
gaps list.

Timings must add up to the slot length, leaving any question time the user asked for.

Iterate on this file alone until the user approves it. Every later artifact derives from it, so a
structural change here is cheap and a structural change after derivation means editing three files.

## Step 4 — Derive the slide outline and participant material

From the approved outline, write:

- `slide-outline.md`, from [`references/slide-outline.md`](references/slide-outline.md). One block per
  slide with **Claim**, **On slide**, **Visual**, **Notes** and **Source**. Notes become speaker
  notes and never appear on the slide. Aim for one slide per one or two minutes.
- `participant-material.md`, from
  [`references/participant-material.md`](references/participant-material.md). The handout: what the
  talk covered, the commands and prompts to try, the pre-flight checklist, where to read more, and
  space for notes. It must make sense to someone who missed the talk.

## Step 5 — Iterate

The user reviews; you revise. For each revision:

- say what changed, per artifact, in a short list;
- carry structural changes through all three: a segment cut from the outline is cut from the slides
  and the handout;
- keep the timings summing to the slot length.

Repeat until the user says the content is done.

## Step 6 — Check before every hand-over

Before showing any draft:

1. Apply the `technical-writing` rules. Then do its two reads: headings only, then each paragraph
   against the ones before it.
2. Re-read every `[S]` quotation against its source.
3. Check that every slide belongs to an outline segment and every outline segment has at least one
   slide.
4. Check that the segment timings sum to the slot length.

Report any check you could not complete, for example a source you can no longer read.

## Step 7 — Render

The Markdown is the version of record. Rendering is done by the harness, not by this skill.

- **The harness can create PowerPoint and Word files** (Claude's pptx and docx skills, ChatGPT file
  generation, Copilot in PowerPoint or Word): render `slide-outline.md` as the deck, with Notes as
  speaker notes, and `participant-material.md` as the Word handout. Offer `presentation-outline.md`
  as a Word speaker's copy. Apply the template or brand from the brief.
- **It cannot:** give the user the three Markdown files and this prompt to paste into Copilot in
  PowerPoint, with `slide-outline.md` attached:

  ```text
  Create a presentation from the attached slide outline. Make one slide per "Slide" block. Put the
  "On slide" text on the slide, follow the "Visual" description, and put "Notes" in the speaker
  notes. Do not add claims that are not in the outline. Use <template or brand from the brief>.
  ```

  For the handout, attach `participant-material.md` to Copilot in Word with: "Format this handout
  as a Word document. Keep every heading, checklist and command exactly as written."

After rendering, tell the user that a rendering can rephrase. Anyone who needs the exact sourced
wording should read the Markdown.

## When the user arrives partway through

- **With a topic and audience already in the prompt:** confirm them in the brief and ask only the
  remaining questions.
- **With an existing outline:** treat it as a draft presentation outline. Check it against the
  sources and rules, then continue from Step 3.
- **Asking only for slides:** still write the brief and outline first, and say why: the slides are
  derived from them.
