---
kind: context
status: open
date: <YYYY-MM-DD>
---

# Waves context: what every track session reads first

**Current wave: <N>.** The lead's session is `wave-<N>-lead`; each track's is `wave-<N>-<track>`.

This file is the living context for <PROJECT>'s waves. Read all of it before anything else, and
re-read it whenever the lead tells you it changed. Where it and `lead.md`, `track.md` or your plan
disagree, this file wins.

Start a session (wave-workflow, "Starting the sessions"):

- Terminal: `claude --agent ai-plugins-and-skills:wave-track --name wave-<N>-<track>`, then `track <track>`.
  The lead uses `wave-lead` and `--name wave-<N>-lead`.
- VS Code extension: a new tab, `/ai-plugins-and-skills:wave-workflow track <track>` (or `lead`), then
  rename the session as above.

Each wave's document lives in planning/waves/wave-<n>/. Its plans are `wave-<n>_<track>_<date>` on
the board.

Last updated: <YYYY-MM-DD>.

## Repositories and skills

- <REPO>: <PATH>, main branch `<MAIN BRANCH>`. <What lives there.>
- Language skills: <e.g. csharp-quality-developer before writing any C#>.

## How we work

- Send the lead (`wave-<N>-lead`) questions, contract proposals and finished results with
  SendMessage.
- Don't push and don't merge. When you have a commit ready, send the lead its SHA. The lead checks
  it from a clean checkout, pushes the branch and opens the pull request.
- Branch from current origin/<MAIN BRANCH>, in your own worktree. A track whose branch has merged
  starts a new branch for follow-up work; never reuse a merged branch.
- A new project joins the solution file in the same pull request that creates it, so build
  validation runs its tests from the first merge.
- Before deleting or moving a file, search the whole repository for links to it.
- Stop at your pause point (Status by track, below), report, and wait. Don't start the next stage
  until the lead says so.

## Where things stand

<What is deployed, what is merged, and the facts a session needs that it cannot get from the files:
environments, identities, ports, naming rules, shapes every track relies on.>

## Rules learned the hard way

<Each rule is a mistake that cost time, written as the rule that prevents it. Keep them across
waves. Starting set, from a nine-track wave that ran:>

- Verify from a clean checkout (a fresh git worktree at your commit, no bin/ or obj/) before you send
  a SHA. A passing local build doesn't prove the commit is complete.
- Before creating a folder, run `git check-ignore -v <path>`. Windows ignores case in .gitignore.
- Integration cleanup must not depend on timing.
- No credentials in tests, not even test ones. Generate them per run.
- When a mutant survives, the test is what's wrong. Strengthen it, re-run, and record both runs.
- A test that pins another track's stand-in fails the day the real code merges. Update it in the
  same pull request as the real code.
- A branch based on main before a solution change doesn't run the projects that change added. When
  you verify such a branch, run those test projects directly as well.

## Status by track: wave <N>

<One line per track: done so far, today's work, and the pause point where it stops and reports.>

- <track>: <status>. Pause point: <where it stops>.
