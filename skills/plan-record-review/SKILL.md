---
name: plan-record-review
description: Records what a plan review actually decided — accepted, merged, rejected, flagged — so the reasoning outlives the conversation. Use immediately after evaluating a review with plan-receive-review, before moving on. Closes the gap where a review is acted on and nothing in the repository says which findings were taken or why the others were not.
metadata:
  category: workflow-composers
  level: beside plan-workflow
  extends: plan-receive-review
---

# Record What the Review Decided

The review pipeline copies a plan out, gets it critiqued, and hands the findings back:
`plan-send-review` deposits, `plan-intake-review` reviews, `plan-receive-review` returns the
findings to the author. The author evaluates each one and reaches a disposition — **accept**,
**merge**, **reject**, **flag**. Then the conversation ends, and unless somebody writes the
outcome down, it is gone.

**That is a real hole.** The review file survives, the revised plan survives, and the mapping
between them — which finding produced which change, and which was argued down — does not. A
directory of completed reviews that says nothing about what any of them changed is the normal
end state, not an unlucky one.

## The asymmetry that makes this matter

An **accepted** finding leaves evidence: the plan changed, and a reader can usually see where.

A **rejected** finding leaves nothing. The plan looks exactly as it did before, and nothing
distinguishes "we considered this and here is why it is wrong" from "nobody noticed." So the same
finding gets raised again, re-argued from scratch, and possibly accepted next time by someone
with less context than the person who rejected it.

**Rejections are the reason this skill exists.** If you record only one thing, record those.

## Three places, in order of authority

### 1. A `## Disposition` section on the review file itself

Appended below the reviewer's findings, under a line saying it is the author's response. **This
is the primary record**, because it is the only one that sits next to the question it answers:
somebody re-reading a finding months from now is holding that file, not the plan and not the log.

`plan-receive-review` files the review under `planning/needs-review/completed/` when it is done
with it. That is where this section goes, and it is the last edit the file ever gets.

```markdown
---

## Disposition

*Appended by the author after evaluating the review. Everything above is the review as received
and is unaltered.*

**N accepted, N merged, N rejected, N flagged** — applied in `<commit>`.

<A paragraph per rejection, with the evidence. Then anything flagged, and how it was ruled.>
```

Set the review document's own `status: done` in the same edit. `plan-writing-syntax` gives a
`review` two statuses, `open` and `done`, and a review that has been dispositioned is done —
leaving it `open` means the board still shows a review awaiting an author who has already
answered it.

**This does not violate the audit trail.** That rule protects the reviewer's *words* from
alteration — never edit a finding, never delete one, never soften one. Appending an attributed
response below them alters nothing, and is the same move the review itself made when it was
appended to a copy of the plan.

**Write it even when there is nothing to say.** A review filed unread, or one whose findings were
all rejected, still gets a section saying so. "Not acted on, set aside by the owner" is a
disposition. An absent section means nobody knows, which is the state this skill exists to end.

### 2. A `## Review History` block in the plan

The summary, for a reader of the plan rather than of the review.

Appended to **the plan of record** — the document on the board, in whichever of `plan-workflow`'s
directories it currently sits. Never to the copy under `needs-review/`, which is an audit trail
and is never edited above the disposition line.

```markdown
## Review History

**v1 reviewed YYYY-MM-DD — <the reviewer's verdict, verbatim>.** N taken: <what, briefly>.

<One paragraph per rejection, with the technical reasoning. This is the part that has to
survive, because nothing else in the repository will carry it.>

<Anything flagged, and how the owner ruled.>
```

Write it in the plan's own voice, as prose. A table of finding-numbers is unreadable six weeks
later, because the numbers refer to a document nobody will open. Reference the review **by name**
rather than by path, the way `plan-writing-syntax` requires — both documents move.

### 3. A commit that says the counts and the reasoning

The commit is what a reader finds when they run `git log` on a file and ask why it looks like
this. Its subject names the round; its body carries the argument. `plan-workflow` puts plan-document
changes under `[PLAN]`.

```
[PLAN] <plan-name> v2 review applied: N taken, M declined

<What was accepted, in one or two sentences.>

<Why each rejection was rejected. Name the evidence — a file, a line, a command you
ran — rather than asserting the reviewer was wrong.>
```

**Verify before you reject.** A rejection recorded without evidence is an opinion that will not
survive contact with the next reviewer.

Two shapes this takes, both of which look perfectly reasonable from the text alone and neither of
which survives thirty seconds in a terminal: a "the test count is wrong" finding that turns out to
be a static count of `it` blocks, missing a parameterised case that expands at run time — and a
request to add a lint step to a checklist, when no linter is configured anywhere in the workspace.
Run the command before you write the paragraph.

## When a review is rejected wholesale

Still record it. `[PLAN] <plan-name> v2 review returned; both findings rejected, no changes` is a
good commit — it says a review happened, was taken seriously, and changed nothing, which is a
different fact from a review nobody read.

## Do not

- **Do not edit the reviewer's findings.** Append below them, attributed. Never reword a finding,
  never delete one, never soften one — the value of the file is that it says what was actually
  said.
- **Do not build an index of dispositions.** A hand-maintained table is a second place for the
  same fact, and the second place is the one that goes stale — it is wrong the first time
  somebody files a review without updating it. The review file carries its own answer.
- **Do not record only counts.** "3 accepted, 2 rejected" without the reasoning is the same hole
  in a smaller font.
- **Do not defer it.** The disposition is clearest in the minutes after the evaluation, and is
  reconstructed badly from a diff a week later — where it can be reconstructed at all.

## Checking

```bash
# reviews filed without a disposition — the state this skill exists to prevent
for f in planning/needs-review/completed/*.md; do
  [ "$(basename "$f")" = README.md ] && continue
  grep -q "^## Disposition" "$f" || echo "no disposition: $f"
done

# reviews still open after their author has answered them
grep -l "^status: open" planning/needs-review/completed/*.md

# plans that have been through review and say nothing about it
grep -rL "## Review History" planning/completed/*.md

# what the log says about a given plan's reviews
git log --oneline --all --grep="review" -i -- planning/
```
