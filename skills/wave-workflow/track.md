# Running a track

You own one track of a wave. The track is the word after `track` in how you were started
(`/ai-plugins-and-skills:wave-workflow track read`, or "track read" as the first message to the
`wave-track` agent). If nobody named one, ask before doing anything else.

## Find your context, in this order

1. **`planning/waves/waves-context.md`, in full.** It gives the current wave *n*, the repositories,
   the main branch, the language skills, and your track's line under Status by track: whether you
   may start, today's work and your pause point. If your line does not say "may start", stop and say
   so. Where this file and the context disagree, the context wins. Re-read it whenever the lead says
   it changed.
2. **The wave document**, `planning/waves/wave-<n>/wave-<n>_<date>.md`: the contracts, who owns
   which files, the calendar.
3. **Your plan**, `wave-<n>_<track>_<date>` or `wave-<n>_<track>-<name>_<date>`. Find it by name
   under `planning/`; it moves between directories. Read every spec section it cites, in full, and
   load every skill it names.

The lead is the session named `wave-<n>-lead`. Name your own session `wave-<n>-<track>`.

## Set up

- Work in your own git worktree (`superpowers:using-git-worktrees`) on branch
  `track/wave-<n>-<track>`, from the current main branch of the repository your plan changes. Never
  touch another session's working copy.
- Move your plan to `planning/in-progress/` with `plan-workflow`: `git mv` and `status:`. Confirm no
  other in-progress plan declares your track or shares a file with you.

## How you work

- `test-filter-development` for every change: a failing test with its captured
  output, the smallest change that passes it, then refactor. Seed a mutant after GREEN.
- Load the language skills the waves context names before writing code in that language.
- Message the lead and nobody else; the lead passes on anything another track needs.
- Edit only the files your plan lists. If you need a file another track owns, stop and tell the
  lead. If its owner needs content from you, write it and send it to the lead.
- Contracts are frozen on the day the wave document says. If your work needs a contract change, ask
  the lead; don't make it.
- Where you depend on work not built yet, code against the frozen contract with the stand-in your
  plan names.
- Commit small, on your branch only, with a `[FEATURE]` or `[PLAN]` prefix. Don't push, and don't
  merge.
- Stop at your pause point, report to the lead, and wait.

## When you finish, or get blocked

- Verify from a clean checkout (a fresh worktree at your commit, no `bin/` or `obj/`) before you send
  a SHA.
- Append the `plan-workflow` Status section: Outcomes, Deviations, Lessons Learned, Backlog Items
  Created.
- Move the plan to `completed/`, or to `blocked/` with `blocked_on:`.
- Send the lead the SHA, the test results (real output), the mutant result, and anything the
  integration window needs to know.
