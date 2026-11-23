---
name: ticket-start
description: Use when someone gives an Azure DevOps work item number and wants it planned, implemented or shipped as a draft PR, as in "start ticket 22744", "plan work item 22531", "take 22744 through to a draft PR". Reads the ticket, creates a fresh ticket branch in its own worktree, writes the plan (plan, the default), implements and verifies it uncommitted (implement), or ships it as a draft PR linked to the ticket by running ticket-end (ship). Each mode picks up where an earlier run stopped, and an earlier mode run again redoes its work. Starts and ends its session through session-workflow, which defers to the repository's and the caller's own guidance and coordinates parallel sessions through session files and resource locks; uses per-session isolated test environments where the repository provides them, and stops cleanly when the Azure login has expired.
metadata:
  category: workflow-composers
---

# Ticket Start

Takes an Azure DevOps (ADO) work item from ticket to plan, to uncommitted implementation, or to a
draft PR, in a worktree of its own. It composes existing skills and holds only what they lack:
ticket lookup and the stop when the Azure login has expired. The commit, push and draft PR are
`ticket-end`'s, which `ship` runs. Its session,
local guidance, locks and modes are `session-workflow`'s.

Invoke it as `/ticket-start {id} [plan | implement | ship] [--in-place]`. `plan` is the default.

## Modes and consent

`/ticket-start` offers `plan`, `implement` and `ship`; what each may change is in `session-workflow`'s
*Modes and consent*. `--in-place` works in the current checkout instead of a worktree, for a
deliberate single session, and still writes a session file and takes locks.

## Running modes in sequence

Each mode ends at a stage: `plan` at *planned*, `implement` at *implemented*, `ship` at *shipped*.
Running a later mode picks up from the stage an earlier run reached. Running a mode whose stage the
ticket has already reached changes nothing. Running a mode whose stage the ticket has passed redoes
that mode's work.

**Read the stage** after step 3, from what exists in the worktree and on the remote. The session file
is no record of it, because `session-end` removes the file.

| Stage | Evidence |
|---|---|
| none | No plan on the board cites the ticket |
| planned | The plan is in the queue root, and nothing else in the worktree has changed |
| implementing | Anything between planned and implemented: the plan in `in-progress/`, code changes beside it, or a plan returned unfinished with a `## Status` section |
| implemented | The plan is in `planning/completed/` with its `## Status` section, and no changed file in the worktree is newer than it |
| shipped | The completed plan is in a commit ahead of the base, the worktree is clean, the branch is pushed with nothing unpushed, and an active PR comes from it |

**Then compare it with the mode's stage:**

- **Not reached yet:** run only the steps after the stage reached. `ship` after a successful
  `implement` goes straight to the commit. `ship` after `plan` implements from the existing plan
  without writing it again. `implement` after an unfinished `implement` resumes where it stopped.
  A changed file newer than a completed plan means the work changed after it was verified: run
  step 6's verification again, update the plan's `## Status` section, then continue.
- **Reached:** nothing to redo. Report the stage, the worktree, the plan's path and, for `ship`,
  the PR URL.
- **Passed:** the caller is asking to redo this mode. Discard the work of every later stage, then
  run the mode's steps again from step 4, so a fresh plan replaces the old one. `plan` after any
  implementation work is the usual case.

**A redo discards only uncommitted work in the ticket's own worktree.**

- First show the caller `git status --short` and the files that will be lost, and wait for
  agreement. An unattended run stops instead.
- If the caller has staged changes, stop and ask, because the reset would unstage them.
- Reset with `git restore --staged --worktree .` and `git clean -fd`. Ignored files, such as build
  output and evidence, stay.
- If the branch has commits ahead of the base, stop and ask instead. Removing committed or pushed
  work would need a force-push, which this skill never does.

Within a stage, each step still checks whether its result exists before it runs: the worktree, the
branch, the plan, the changes, a commit ahead of the base, the pushed branch and the PR.

## Precedence and local guidance

Follow `session-workflow`'s *Precedence and local guidance*, and record what applies in the plan's
*Local guidance* section (see `references/plan-sections.md`).

## Steps

Run the script verbs from the repository, with the paths resolved as under *Script reference*: the
ADO verbs run `ticket.ps1`, and the session and lock verbs run `session.ps1`. The session ID comes
from `session-start`; call `session-step` as each step begins.

1. **Preflight** (every mode).
   - Run `repo`. Exit 3 means the Azure login has expired: follow *Expired Azure login* before
     anything else. Exit 4 means the remote is not ADO.
   - Run `session-workflow start` with `-Subject ticket:{id}` and the mode. It runs `sessions` and
     `session-start`, and discovers local guidance. Exit 2 from `session-start` means a session
     file for this repository and ticket exists; follow `session-workflow` on it. An unattended run
     stops.
   - With `--in-place` only, check the working tree. If it has changes and the current branch is not
     this ticket's, stop and ask. By default the caller's checkout is never touched.
2. **Read the ticket** (every mode). Run `workitem -Id {id}`. Summarize the type, title, state,
   description, acceptance criteria and repro steps. Flag missing acceptance criteria rather than
   inventing them. A description is HTML; read it as text. Never edit the work item.
3. **Branch and worktree** (every mode).
   - Name the branch from, in order: local guidance; the repository's observed convention, taken
     from recent remote branch names and PR titles; then `branch-name`, which gives
     `feature/{id}_{slug}` for a User Story and `bug/{id}_{slug}` for a Bug. For any other type
     (exit 4), ask.
   - Run `git fetch origin`, then create the worktree for a fresh branch from `origin/{defaultBranch}`
     with `superpowers:using-git-worktrees`, or plainly
     `git worktree add --no-track -b {branch} {path} origin/{defaultBranch}`. `defaultBranch` comes
     from `repo`, because a local `origin/HEAD` can be stale.
   - Directory choice follows `using-git-worktrees`: local guidance, then an existing `.worktrees/`
     or `worktrees/`, then `.worktrees/`. Ignore it through `.git/info/exclude`. Never edit
     `.gitignore` and never commit to make the directory ignored.
   - If the branch exists, resume in its worktree, adding one for the existing branch if none is
     left. If git reports it checked out in another worktree, stop and report that.
   - With `--in-place`, run `git switch --no-track -c {branch} origin/{defaultBranch}` instead.
   - From here, work only inside the worktree. Every stop prints its path.
4. **Plan** (every mode; skipped once the ticket is *planned* or later, unless this run redoes
   `plan`).
   - Run `plan-tier`, then write the plan to the `planning/` board's queue root in the worktree,
     following `plan-workflow` for its place on the board and `plan-writing-syntax` for its name and
     frontmatter. Create the board's directories where the repository has none. Where local
     guidance puts plans somewhere else, follow it instead and skip the board moves in later
     steps. The plan never goes in an ignored directory, and `plan` never commits it.
   - Cite the ticket number and URL. Include the sections in `references/plan-sections.md`.
   - For a brownfield or multi-subsystem ticket, research first with `ace-fca-workflow`.
   - `plan` stops here.
5. **Review gate** (`implement`, `ship`). A high `plan-tier` tier requires adversarial plan review
   before any code, through `plan-send-review`, `plan-intake-review`, `plan-receive-review` and
   `plan-record-review`. If the review has not happened, stop.
6. **Implement and verify** (`implement`, `ship`; skipped once the ticket is *implemented*, except
   to verify again a change newer than the completed plan).
   - Move the plan to `planning/in-progress/` before any code is written, as `plan-workflow`
     describes, and implement from it. Every board move in this step changes the file's directory
     and its `status:` together. Move the file itself, not with `git mv` or `plan-doc.cs close`,
     because `implement` never stages. Resuming, leave the plan where it is if it is already there.
   - Install what the worktree needs, as local guidance describes. A new worktree has no installed
     dependencies, and this takes minutes.
   - Follow the precedence above with `test-filter-development` and the domain skills.
   - Where local guidance provides an isolated per-session environment for tests and end-to-end
     runs, create it when verification starts, and run tests and end-to-end checks only inside it
     (see *Isolated environments*). Take a lock before each use of any resource that remains
     shared, and release it after (see *Parallel sessions*).
   - Verify with `superpowers:verification-before-completion`. Report build, standards check and
     tests separately, with actual outcomes.
   - When every change is implemented and verification passes, complete the plan as
     `plan-workflow` describes: append its `## Status` section, set `status: completed` and
     `completed:`, and move it to `planning/completed/`. Check the board with
     `plan-doc.cs validate --path planning`.
   - When verification fails, or the work stops before it is done, the plan does not move to
     `completed/`. Follow `plan-workflow` for a plan that leaves `in-progress/` unfinished, and say
     where it stands in the handover.
   - `implement` stops here with the handover in `references/handover.md`.
7. **Ship** (`ship`). Run `ticket-end` for the ticket. It commits under its commit rules,
   completes the plan at the PR with post-PR checks as `verify` reminders, pushes without force,
   opens the draft PR through `ado-pr` with the work item linked, and ends the session. Its exit
   code is `ship`'s, passed through unchanged, and its last line, the PR URL, is `ship`'s last line.
8. **Finish** (`plan`, `implement`). Run `session-workflow end`: it removes the session's isolated
   environment, then runs `session-end`, which releases every lock the session holds and removes its
   file. Every earlier stop does the same. The worktree stays for review.
   - `plan` ends with the worktree path and the plan's path.
   - `implement` ends with the worktree path and the handover.

## Parallel sessions

The session file, locks and stale files are `session-workflow`'s: a ticket session's subject is
`ticket:{id}`, and every lock follows its *Locks* section.

## Isolated environments

Where local guidance gives each session its own disposable environment for tests and end-to-end
runs, `session-workflow` keeps the session's record of it and removes it at every stop, under its
*Isolated environments*. The rules for the environment itself, a clean start and never widening it,
are in `test-filter-development`'s reference, `isolated-environments.md`.

## Expired Azure login

Any script verb that reaches ADO exits 3 when the token call fails or mentions `az login`, `AADSTS`
or a refresh token, when ADO answers 401 or 203, or when it returns an HTML page where JSON was
expected. The JSON then carries `loginCommand`, and `tenant` when the Azure CLI still has one cached. On exit 3:

- Stop immediately. Do not retry, do not run `az login` (it is interactive), do not guess the
  ticket's contents, and do not continue work that depends on it.
- If the JSON has `tenant`, give the caller `loginCommand` as it is. If it has no `tenant`, ask the
  caller for their tenant, then give them `az login --tenant {their answer}`.
- Name the step that stopped and what is already done. The next invocation resumes there.
- Run `session-workflow end`, as at any other stop.

## Script reference

```powershell
$tk = if ($env:CLAUDE_PLUGIN_ROOT) {
  "$env:CLAUDE_PLUGIN_ROOT\skills\ticket-start\scripts"
} else {
  "$HOME\.claude\skills\ticket-start\scripts"
}
& "$tk\ticket.ps1" workitem -Id 22744

$ss = if ($env:CLAUDE_PLUGIN_ROOT) {
  "$env:CLAUDE_PLUGIN_ROOT\skills\session-workflow\scripts"
} else {
  "$HOME\.claude\skills\session-workflow\scripts"
}
& "$ss\session.ps1" sessions
```

`ticket.ps1` needs PowerShell 7 and the Azure CLI, logged in. The org, project and repository come
from `origin`, in its HTTPS or SSH form. The result is JSON on stdout.

| Verb | Does |
|---|---|
| `repo` | Reads `origin` and the server's `defaultBranch` |
| `workitem -Id` | Type, title, state, fields, ticket URL, team project and relations |
| `branch-name -Id -Type -Title` | The fallback branch name |
| `pr-find -Branch` | Active PRs from the branch, for the *shipped* stage check |
| `session-find -Id -Repo` | The ticket's session, for `ticket-end` |

The draft PR is opened through `ado-pr`, by `ticket-end`; `ticket.ps1` has no verb for it.

The session and lock verbs (`session-start`, `session-step`, `session-end`, `sessions`,
`lock-acquire`, `lock-status`, `lock-release`, `lock-break`) run `session.ps1`, from
`session-workflow`. Its *Script reference* lists them.

| Exit | Means |
|---|---|
| 0 | Done |
| 1 | Unexpected error, or the Azure CLI is missing |
| 2 | Busy or conflicting, from `session.ps1`: a lock is held, or a session file exists for the ticket |
| 3 | The Azure login has expired; the JSON carries `loginCommand`, and `tenant` when one is cached |
| 4 | Refused input: not an ADO remote, an unsupported type, a session `ticket-end` refuses; from `session.ps1`, a malformed name or subject, an unknown mode or session |

The script never edits a work item, merges, or runs `az login`. Its battery is
`test/Test-TicketScript.ps1`, which exits 0 only when every case passes.

## Do not

- Stash, discard or unstage anyone's work. The one exception is a redo the caller has agreed to (see
  *Running modes in sequence*).
- Force-push, merge, set auto-complete or add reviewers.
- Edit the work item.
- Run `az login`.
- Delete another session's file, lock or isolated environment silently.
- Leave an isolated environment running at a stop, or widen its isolation to make a check pass.
- Touch the caller's own checkout, except under `--in-place`.
- Edit `.gitignore` or commit to make a worktree directory ignored.
- Run `az boards` or `az repos`. They need an extension that may be missing, and its install prompt
  hangs a non-interactive shell.
