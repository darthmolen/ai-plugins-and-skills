---
name: session-workflow
description: Use when a session is about to work in a repository alongside other sessions — a ticket from ticket-start, a plan being executed, or a wave's lead or track — to start it and end it. Start writes the session file with its subject (ticket:22744, plan:{name}, wave:wave-1/auth) and mode (plan, implement, commit, ship, lead), and reads local guidance. During the session it takes and releases locks on shared resources such as ports, local databases and containers, and finds stale sessions and locks. End releases the session's locks and removes its file, at every stop. Defines the modes and the consent each grants. Drivers such as ticket-start pick the subject and the mode.
metadata:
  category: workflow-composers
---

# Session Workflow

One session on one machine: its session file, its mode, local guidance, the locks it holds on
shared resources, and its end. It sits between `plan-workflow`, which says what the work is, and
`wave-workflow`, which runs many sessions under a lead.

A **driver** supplies the session's **subject**, what it works on, and picks its mode: `ticket-start` for an
Azure DevOps work item, a plan document, or a wave's lead or track. This skill has no notion of a
ticket, a plan's stages or a wave's tracks; it records them as the session's subject.

## Start and end

**`session-workflow start`**, run by the driver before any work:

1. Run `sessions` to see the other sessions and locks on this machine.
2. Run `session-start` with the subject, the worktree, the branch and the mode. Exit 2 means a
   session file for this repository and subject exists. Show it to the caller. If it belongs to a
   running session, stop. If it is a leftover, start again with `-Replace` and its ID, only with the
   caller's agreement. An unattended run stops.
3. Discover local guidance (see *Precedence and local guidance*).
4. Call `session-step` as each step of the driver begins.

**`session-workflow end`**, at every stop: a finish, a failed step, an expired login, a refused
push.

1. Remove the session's isolated environment, if it has one (see *Isolated environments*).
2. Run `session-end`. It releases every lock the session holds and removes its file, and touches
   nothing belonging to another session.

The worktree stays. Removing it is the caller's decision.

## Modes and consent

The modes from `plan` to `ship` are cumulative. Each runs everything the one before it does, then
adds its own steps. `lead` stands apart.

| Mode | Does | Changes |
|---|---|---|
| `plan` | Reads the ticket, creates the branch in its own worktree, and writes the plan to the `planning/` board's queue | The worktree, the local branch and the plan documents. No commit, no code, configuration or test changes |
| `implement` | Everything `plan` does, then moves the plan to `in-progress/`, implements and verifies it, and moves the plan to `completed/` | Also the code, configuration and test changes and the plan's moves, unstaged and uncommitted. Never stages, unstages, commits or pushes, even where local guidance commits automatically |
| `commit` | Everything `implement` does, then commits on the session's own branch | Also the commits on that branch. Never pushes, merges or opens a PR. A wave track runs in this mode |
| `ship` | Everything `implement` does, then commits, pushes and opens a draft PR | Also the commit, which carries the completed plan with the code, the push and the draft PR. The PR URL is the last line |
| `lead` | Verifies other sessions' commits in verification worktrees, pushes verified branches and opens their PRs (`pr-create`) | Verification worktrees, the coordination documents (wave and planning documents), pushes and PRs. Never writes production code, force-pushes or merges. A wave lead runs in this mode |

The `plan`, `implement` and `ship` rows are `ticket-start`'s, unchanged, because they are its
contract. For a plan or wave subject, read "the ticket" as the subject. `ship` includes everything
`commit` does.

Starting a session in a mode authorizes that mode's changes for that subject and nothing wider.
Every mode also writes its own session file and takes its own locks. `session-start` accepts these
five modes and refuses any other with exit 4 (`bad-mode`). The script records the mode; holding to
its consent is the session's job.

A driver offers the modes that fit its work. `ticket-start` offers `plan`, `implement` and `ship`.
A wave track uses `commit`, and its lead uses `lead`.

## The session file

Every session writes one when it starts, because another may start later. The files live in
`~/.agent-sessions/`, outside every repository and never committed, with locks in
`~/.agent-sessions/locks/`. `AGENT_SESSIONS_DIR` overrides the directory.

| Field | Holds |
|---|---|
| `session` | The ID: `{repo}_{key}_{yyyyMMddTHHmmssfff}_{4 hex}` |
| `subject` | What the session works on, as `<kind>:<value>` |
| `repo` | The repository name |
| `ticket` | The ticket's number for a `ticket` subject, and `null` for any other |
| `worktree` | The worktree's full path |
| `branch` | The branch |
| `mode` | One of the five modes |
| `step` | The step in progress, from `session-step`. Starts as `preflight` |
| `started`, `updated` | UTC timestamps, ISO 8601. `session-step` refreshes `updated` |

**The subject.** `<kind>` is `ticket`, `plan` or `wave`, and `<value>` is not empty:

| Subject | For |
|---|---|
| `ticket:22744` | A ticket, a GitHub issue or an Azure DevOps work item. The value is its number, without `#` or `AB#` |
| `plan:session-workflow-extraction_2026-11-19` | A plan, by its name |
| `wave:wave-1/auth` | A wave track or lead, as `wave-<n>/<track>` |

Any other kind, an empty value or a missing colon exits 4 (`bad-subject`). A ticket's value that is
not a number exits 4 (`bad-id`). `-Ticket 22744` is shorthand for `-Subject ticket:22744`, and
passing both is refused. A ticket's ID key is its number; any other subject is folded into the
characters an ID allows, so `wave:wave-1/auth` gives `wave-wave-1-auth`.

One live session per repository and subject. A second `session-start` on the same pair exits 2.

**Older session files.** A file that carries `context` instead of `subject` reads as that
subject. A file with `ticket` and no `subject` at all reads as `ticket:<ticket>`. Either way
`sessions` reports it under that subject, it blocks a new session on the same subject, and
`session-end` ends it. New files keep the `ticket` field so a reader that knows only tickets still
finds it.

## Locks

A shared resource is anything one session's use can disturb for another: a fixed port, a local
service or container, a shared database, or anything local guidance says must not run in parallel.
Before each use:

1. Run `lock-status -Resource {name}`. Exit 0 is free, exit 2 is held.
2. If held, wait through the harness's wait mechanism, not a loop in the script (in Claude Code,
   Monitor with an until-loop around `lock-status`). After 30 minutes, ask the caller. On an
   unattended run, report the step as blocked.
3. Run `lock-acquire -Resource {name} -Session {id} -Purpose {text}`. Exit 2 means another session
   won the race: go back to step 2.
4. Run `lock-release` as soon as the use ends, whether it succeeded or failed.

`lock-acquire` creates the lock file exclusively, so two sessions can never both hold it.

Both sessions must derive the same name for the same resource, or the lock protects nothing. Take
the name from the configuration that addresses the resource, lowercased: `port-{n}`,
`sql-{host}-{port}-{database}`, `container-{name}` or `service-{name}`.

`plan-workflow` prevents collisions on files, before work starts, by admitting a plan to a track only
when its file set is disjoint. Locks cover what is shared only while something runs.

## Stale sessions and locks

A session can die without cleaning up. `sessions` flags one as likely stale only when its last
reported step and its worktree's newest changed file are both older than 30 minutes, which is above
the longest use seen (an 11-minute functional run). A lock is likely stale when its session file is
gone or its session is likely stale.

Never delete another session's file or lock silently. Show the caller what is there and ask. Only
with their agreement run `lock-break -Resource {name}`, or `session-start -Replace`. An unattended
run reports it and treats the resource as busy.

## Isolated environments

Where local guidance gives each session its own disposable environment for tests and end-to-end
runs, the session keeps track of it. What the environment is, and what it may and may not hold, is
in `test-filter-development`'s reference, `isolated-environments.md`.

- **Capacity.** The environment's own claim mechanism limits how many exist. When none is free,
  wait through the harness's wait mechanism as for a held lock, ask the caller after 30 minutes,
  and on an unattended run report the step as blocked.
- **Record it.** Call `session-step` with the environment's name when it is created, so the session
  file shows which environment the session holds. The next session's `sessions` output and the
  guidance's list command then identify a leftover.
- **Always remove it.** `session-workflow end` removes it at every stop: a finish, a failed step, an
  expired login, a refused push. Collect evidence first, as the guidance says. Never remove another
  session's environment silently; show the caller and ask, as for a stale lock. The reference gives
  the reasons for both.
- **Still lock the shared.** Anything outside the environment that two sessions can disturb, such
  as the caller's own running services or a shared database, still takes a lock (see *Locks*), or
  is left alone when guidance says agents must not touch it.

## Precedence and local guidance

From highest to lowest:

1. What the caller says in the session, including the mode.
2. Local guidance: the repository's instruction files and the caller's own.
3. The driver's steps and fallbacks.
4. The style and domain skills it chains, such as `csharp-quality-developer`,
   `typescript-developer`, `angular-developer`, `technical-writing` and `test-filter-development`.
   When one conflicts with local guidance, local guidance wins and the plan says so.

This skill names no person's or repository's guidance and carries none of it.

**Discovery.** Read these directly; harnesses load nested files unevenly.

- The worktree's tracked guidance (`AGENTS.md`, `CLAUDE.md`, nested ones on the paths the work is
  likely to touch). It holds the base branch's current version.
- Every directory above the worktree up to the home directory. A worktree inside the caller's
  checkout passes through that checkout; read only the files git does not track there, because its
  tracked files are another branch's copy of what the worktree already has. A caller's own ignored
  instruction file sits there and is absent from a fresh worktree.
- The user-level instruction files the harness supports, such as `~/.claude/CLAUDE.md`.
- `.editorconfig` (root and nested), analyzer settings and `Directory.Build.props`, or the
  ecosystem's equivalents.

**Checkpoints.** Local guidance most often differs on these. Check each and record what applies:

- branch naming and the base branch
- where plans live, and their format
- commit, staging and auto-commit policy
- the build, format, analyzer and test commands, and how tests must run (sequentially, for example)
- end-to-end testing expectations and any existing harness
- whether tests and end-to-end runs use an isolated per-session environment, such as a container
  stack, and its commands to create, use, list, prune and remove it, or share local resources that
  need locks
- documentation expectations
- PR title and description conventions
- what the agent may touch: databases, local services, shared environments, credentials

**Reporting.** The plan records what applies. Ask the caller only when two sources contradict each
other, or when nothing covers something material. Where asking is impossible, as on an unattended
run, follow local guidance, take the more conservative reading, and record the assumption in the
handover.

**Silence.** Where guidance is silent, follow the repository's existing conventions and the chained
skills, and state in the plan what was assumed. The skill has no rulebook of its own.

**Permissions stay local.** Anything local guidance authorizes, such as resetting a local database,
applies in that repository only.

A rule about where evidence or artifacts go governs evidence, never the plan, unless it names plans.

## Script reference

```powershell
$ss = if ($env:CLAUDE_PLUGIN_ROOT) {
  "$env:CLAUDE_PLUGIN_ROOT\skills\session-workflow\scripts"
} else {
  "$HOME\.claude\skills\session-workflow\scripts"
}
& "$ss\session.ps1" session-start -Repo Contoso.Orders -Subject ticket:22744 `
    -Worktree D:\dev\Contoso.Orders\.worktrees\22744 -Branch bug/22744_x -Mode implement
```

It needs PowerShell 7. The result is JSON on stdout.

| Verb | Does |
|---|---|
| `session-start -Repo (-Subject \| -Ticket) -Worktree -Branch -Mode [-Replace]` | Writes the session file; refuses a second for the same repository and subject |
| `session-step -Session -Step` | Records the step in progress |
| `session-end -Session` | Releases the session's locks and removes its file |
| `sessions [-StaleAfterMinutes 30]` | Lists sessions, with their subject, and locks, with ages and a likely-stale flag |
| `lock-acquire -Resource -Session -Purpose` | Creates the lock exclusively |
| `lock-status -Resource` | Free or held |
| `lock-release -Resource -Session` | Removes the lock only if this session holds it |
| `lock-break -Resource` | Removes any lock, after the caller agrees the holder is gone |

| Exit | Means |
|---|---|
| 0 | Done (`lock-status`: free) |
| 1 | Unexpected error |
| 2 | Busy or conflicting: a lock is held, or a session file exists for the subject |
| 4 | Refused input: a malformed name or subject, an unknown mode or session |

Exit 3 is `ticket-start`'s, for an expired Azure login; the numbering is shared so a caller
branches on one table. Its battery is `test/Test-SessionScript.ps1`, which exits 0 only when every
case passes.

## Do not

- Delete another session's file, lock or isolated environment silently.
- Leave a session file, lock or isolated environment behind at a stop.
- Take a lock under a name not derived from the resource's address.
- Wait for a lock with a loop in the script, or past 30 minutes without asking.
- Act outside the session's mode.
