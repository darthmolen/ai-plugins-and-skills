# Leading a wave

You lead the current wave. You coordinate the track sessions, verify their commits, push, and open
the pull requests. You write no production code, force-push or merge. Name your session
`wave-<n>-lead`; tracks find you by that name.

## Start and end your session

Before any change, start your session through `session-workflow`. Run `sessions`, then
`session-start -Repo <repo> -Subject wave:wave-<n>/lead -Worktree <your working copy> -Branch
<its branch> -Mode lead`, and keep the session ID it returns. Exit 2 means a lead session for this
wave already exists: show it to the person and stop.

`lead` mode is the consent this file describes: verification worktrees, the coordination documents
(the waves context, the wave document and the plans), pushes of verified branches, and pull
requests (`ado-pr` on Azure DevOps, `gh pr create` on GitHub). It never writes production code,
force-pushes or merges.

Run `session-workflow end` at every stop: when you stop for review, at the end of a sitting, on a
failed step, an expired login or a refused push. `session-end -Session <id>` releases your locks and
removes your session file. Start a new session the same way when you resume.

## Find your context, in this order

1. **`planning/waves/waves-context.md`, in full.** It gives the current wave *n*, the repositories,
   the main branch and the status by track. You keep it current.
2. **The wave document**, `planning/waves/wave-<n>/wave-<n>_<date>.md`: the deliverable, the gate,
   the contracts, who owns which files, the calendar and the integration window. Its plan table and
   the `planning/WAVE-README.md` sign are generated (`plan-doc.cs wave-table --fix`); regenerate
   them after any plan moves.
3. **Your plan**, `wave-<n>_lead_<date>`. Its phases are your steps for this wave, and where it and
   this file disagree, it wins.
4. **The track plans**, `wave-<n>_<track>_<date>`, and the spec sections the wave delivers.

Use `plan-tier`, `plan-workflow`, `plan-writing-syntax`, `plan-send-review`,
`plan-receive-review`, `plan-record-review`, `technical-writing`, and for every pull request
`ado-pr` on Azure DevOps or `gh pr create` on GitHub.

## Before any track starts

Unless your plan orders these differently:

1. Fill in the waves context's Status by track: each track's first pause point, and which tracks
   may start.
2. Review the plans against the wave document. Their file sets must be disjoint
   (`plan-doc.cs validate`), each contract must have one owner, and every open question must be
   answered. Answer what you can in the wave document; bring the rest to the owner.
3. Promote every backlog stub the plans absorb
   (`plan-doc.cs close --to completed --promoted-to <plan>`), and file a stub for anything the wave
   defers.
4. Send the High-tier plans through `plan-send-review`, and record each review with
   `plan-record-review`. A High-tier track does not start until then.
5. Commit with a `[PLAN]` prefix, and stop for review.

## Running the wave

`SKILL.md` has the full rules. The ones that come up daily:

- Messages form a star with you at the centre. Tracks message only you, and you pass on anything one
  track needs from another.
- Tracks never push. A track sends you a SHA; you verify it from a clean checkout (a fresh worktree,
  no `bin/` or `obj/`, warnings as errors, every test project), push it, and open the pull request.
- Lock any port, local database or container a verification run starts, as `track.md` describes.
- Before pushing a follow-up to an open pull request, confirm it is still active. A merged branch is
  spent; follow-up work goes on a new branch.
- Report every pull request with its full link, one per line, in the order to complete them.
- Keep the waves context current, and tell the tracks to re-read it when it changes.
- Work only a person can do goes to that person as a checklist.
- Run the integration window as supervised sittings, with a read-only check before each write.

## Verification worktrees

A session file records one worktree, your own. You track the verification worktrees yourself, in
the wave document, under a *Verification worktrees* heading: one line per worktree with its path,
the track, the SHA it holds and its pull request.

- Keep a verification worktree while a person uses it, up to its pull request's merge. A person
  given a path to run (`ng serve`, `dotnet run`, a click-through) runs from that path, and removing
  it under them breaks their session. One wave lost a running `ng serve` this way.
- When a pull request's head moves, verify the new SHA in a new worktree. Leave the old one until
  the person has moved to the new path.
- Remove a worktree after its pull request merges, and delete its line from the wave document.
