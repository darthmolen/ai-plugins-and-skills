# Session Workflow

A session is one AI agent working in one worktree of a repository on your machine. When several
agents work on one machine at once, each needs to know what the others are doing: which ticket or
plan they hold, which port or local database they are using, and whether they are still running.
The `session-workflow` skill gives each session a file that answers those questions, a mode that
says what it may change, and locks on the resources it shares with the others.

This page covers what a person sees and decides. The rules the agent follows are in the
[`session-workflow` skill](../../skills/session-workflow/SKILL.md). Why the layer exists, and why
it sits under tickets and waves rather than above them, is in
[VOCABULARY.md](../VOCABULARY.md#a-session-layer-between-plans-and-waves).

---

## Where the session layer sits

```text
plan-workflow ──── the board: what the work is, its document, its status
       │
session-workflow ─ one session on one machine: its file, its mode, local guidance,
       │           locks, stale detection, and its end at every stop
       │
wave-workflow ──── many sessions under a lead; the lead and each track is one session
```

`session-workflow` does not know what a ticket, a plan's stages or a wave's tracks are. A **driver**
starts the session, names its **subject**, what it works on, and picks its mode:

| You are working on | The driver | The session's subject |
|---|---|---|
| An Azure DevOps work item | `/ticket-start {id}` | `ticket:22744` |
| A plan you execute yourself | you, through `session-workflow start` | `plan:<plan name>`, such as `plan:auth-refactor_2026-06-22` |
| A wave track | the track's instructions, `track.md` | `wave:wave-<n>/<track>`, such as `wave:wave-2/api` |
| A wave lead | the lead's instructions, `lead.md` | `wave:wave-<n>/lead` |

One repository has at most one live session per subject. A second session for the same ticket,
plan or track in the same repository is refused.

---

## Modes and what each lets the agent change

The mode is your consent. Starting a session in a mode lets the agent make that mode's changes for
that subject and nothing more. The modes from `plan` to `ship` build on each other: each does
everything the one before it does, then adds its own steps. `lead` stands apart.

| Mode | The agent may | It never |
|---|---|---|
| `plan` | Create the branch in its own worktree and write the plan to the board's queue | Commits, or changes code, configuration or tests |
| `implement` | Also move the plan to `in-progress/`, implement and verify it, and move the plan to `completed/` | Stages, commits or pushes. The changes are left uncommitted for you to review |
| `commit` | Also commit on the session's own branch | Pushes, merges or opens a pull request |
| `ship` | Also commit, push, and open a draft pull request | Merges, force-pushes or adds reviewers |
| `lead` | Verify other sessions' commits in separate worktrees, edit the wave and planning documents, push verified branches and open their pull requests | Writes production code, force-pushes or merges |

Each driver offers the modes that fit its work:

- `/ticket-start` offers `plan` (the default), `implement` and `ship`.
- A wave track runs in `commit`, and a wave lead in `lead`.
- For a plan you run yourself, you choose.

The session file records the mode. Keeping to it is the agent's job; no script stops an agent from
acting outside it.

---

## Starting a session

### For a ticket

```text
/ticket-start 22744 implement
```

`ticket-start` reads the work item, creates a branch in a new worktree, and starts a session with
the subject `ticket:22744`. Running it again with a later mode continues from where the earlier run
stopped: `ship` after a successful `implement` goes straight to the commit. The
[`ticket-start` skill](../../skills/ticket-start/SKILL.md) describes how it reads the stage a ticket
has reached.

### For a wave track or lead

Start the session the way [WAVE-WORKFLOW.md](WAVE-WORKFLOW.md) describes. Once the track or lead
has its worktree, and before it changes anything, its instructions (`track.md` or `lead.md`) start
the session. In Claude Code, the plugin's `wave-session-title` hook names the session; it does not
start it.

### For a plan on a branch of your own

Tell the agent to start a session through `session-workflow`, with the plan's name and a mode:

```text
Start a session with session-workflow: subject plan:auth-refactor_2026-06-22, mode commit.
```

The agent lists the sessions already on the machine, writes its session file, and reads the local
guidance before it does any work.

### What starting does

1. Lists the other sessions and locks on the machine.
2. Writes the session file. If one already exists for this repository and subject, the agent shows
   it to you and stops. If that session is still running, leave it. If it is a leftover, the agent
   replaces it only when you agree.
3. Reads local guidance: the repository's `CLAUDE.md` and `AGENTS.md` files, your own instruction
   files, and `.editorconfig` and analyzer settings. What you say in the session overrides local
   guidance, and local guidance overrides the skill's defaults.

Session files live in `~/.agent-sessions/`, outside every repository, and are never committed.
`AGENT_SESSIONS_DIR` changes the directory.

---

## Locks on ports, databases and containers

Two running plans never change the same file, because `plan-workflow` admits a plan only when no
running plan lists the same files. Their sessions can still collide at run time: two test
runs that both start a server on port 5000, or two migrations against one local database.

Before a session runs something that uses a shared resource, it takes a lock on it, and it releases
the lock as soon as the run ends, whether the run passed or failed. A session that finds the lock
held waits. After 30 minutes it asks you what to do, and an unattended run reports the step as
blocked.

A lock is named after the resource's address, in lower case: `port-5000`,
`sql-localhost-1433-orders`, `container-redis`, `service-rabbitmq`. Both sessions must derive the
same name, or neither blocks the other. A name that includes the session's own ticket or track,
such as `wave-2-api-port`, protects nothing.

---

## Stale sessions and locks

A session can die without ending, and leave its file and locks behind. A session is flagged as likely stale when both its last recorded step and the newest changed file in
its worktree are more than 30 minutes old. A lock is likely stale when its session is gone or
likely stale.

An agent never removes another session's file or lock on its own. It shows you what it found and
asks. Only with your agreement does it break the lock or replace the session file. An unattended run
reports what it found and treats the resource as busy.

---

## Isolated test environments

Some repositories give each session its own disposable environment for tests and end-to-end runs,
such as a container stack. Inside it, nothing is shared, so the session takes no locks for it and
two sessions can use the same port numbers. Anything outside it, such as your own running services
or a shared database, still needs a lock.

The session records which environment it holds, and removes it at every stop. An agent never
removes another session's environment without asking you. The rules for the environment itself, a
clean start and never widening its isolation to make a test pass, are in
[`test-filter-development`'s isolated-environments reference](../../skills/test-filter-development/isolated-environments.md).

---

## Ending a session

Every stop ends the session: a finish, a failed step, an expired `az` or `gh` login, a refused push. Ending
removes the session's isolated environment, releases every lock it holds, and removes its session
file. The worktree stays, so you can review it. Removing it is your decision.

How the work leaves the session depends on the driver and the mode.

### A ticket: `/ticket-ship`

```text
/ticket-ship 22744
/ticket-ship #412
```

`ticket-ship` turns a ticket's finished work into a draft pull request:

1. Finds the ticket's session. It accepts one in `implement` or `ship` mode, and refuses one in any
   other mode with exit 4, because a `plan` session has built nothing. With no session, it starts
   one in `ship` mode.
2. Verifies the build and tests. A failure stops it before any commit.
3. Commits the work with the completed plan, and turns any check that can only happen after the
   pull request into a `verify` reminder.
4. Pushes, never with force.
5. Opens a draft pull request through `pr-create`, linked to the issue or work item.
6. Ends the session, and prints the pull request URL as its last line.

Running `/ticket-ship` is your consent to commit and push, even when the session started in
`implement`. `/ticket-start 22744 ship` runs the same steps by handing over to `ticket-ship`. A rerun
after a failure picks up where it stopped: `pr-create` returns the pull request the branch already has
instead of opening a second.

### A plan: `plan-workflow`'s close, then `pr-create`

When a plan's work is done, `plan-workflow` completes it: the Status block, the move to
`completed/`, and any `verify` reminders, in the same pull request as the code. Then:

- A session in `ship` or `lead` pushes the branch and raises the pull request with
  `pr-create`, on Azure DevOps or GitHub.
- A session in `plan`, `implement` or `commit` stops at the close. Pushing is yours, or the wave
  lead's.

`pr-create` reads the host from the remote. It opens drafts and links work items or issues when
asked, and returns an existing open pull request from the branch rather than opening another.

### A wave track or lead

A track ends its session at every pause point, block and finish, and starts a new one when the lead
resumes it. It never pushes: it sends the lead a verified commit. The lead ends its session at the
end of each sitting.

The session file records one worktree. The lead's verification worktrees are not registered with
the session; the lead lists them in the wave document, under *Verification worktrees*, and removes
each one after its pull request merges.

### By hand

Tell the agent to end the session through `session-workflow`. Do this when you stop work on a plan
you started a session for, so the next session on the machine does not find a leftover.

---

## Skills

- [`session-workflow`](../../skills/session-workflow/SKILL.md): the session file, modes, locks,
  stale detection and local guidance
- [`ticket-start`](../../skills/ticket-start/SKILL.md) and
  [`ticket-ship`](../../skills/ticket-ship/SKILL.md): the driver for tickets, GitHub issues and Azure
  DevOps work items
- [`plan-workflow`](../../skills/plan-workflow/SKILL.md): the board, and the close that hands off to
  the pull request
- [`pr-create`](../../skills/pr-create/SKILL.md): pull requests on Azure DevOps and GitHub, drafts and work-item links
- [`wave-workflow`](../../skills/wave-workflow/SKILL.md): many sessions under a lead
- [`test-filter-development`](../../skills/test-filter-development/SKILL.md): tests, and isolated
  environments
