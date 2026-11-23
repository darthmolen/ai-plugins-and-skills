---
name: ticket-ship
description: Use when a ticket's work — a GitHub issue or an Azure DevOps work item — is implemented and verified and should become a draft PR, as in "ship ticket 22744", "ship issue 412 now", or when /ticket-start {id} ship reaches its commit. Finds the ticket's session and worktree, refuses a session still in plan mode, verifies, commits under the commit rules, completes the plan at the PR with post-PR checks as verify reminders, pushes without force, opens a draft PR through pr-create with the ticket linked, and ends the session through session-workflow at every stop. Prints the PR URL as the last line.
metadata:
  category: workflow-composers
---

# Ticket Ship

Ships a ticket's work as a draft PR: commit, push, the draft PR linked to the ticket, and the
session's end. `ticket-start` begins a ticket; this skill ships it. The ticket is a GitHub issue or
an Azure DevOps work item, given in any form `ticket-start`'s *Where the ticket lives* accepts.
`/ticket-start {id} ship` runs its own steps 1 to 6 and then runs this skill, so the commit rules
live here and only here.

Invoke it as `/ticket-ship {ticket}`. It also ships a ticket whose session started in place or by
hand. Below, `{id}` is the ticket's number without `#` or `AB#`.

## Contract

- **Input.** `/ticket-ship {ticket}`.
- **Mode.** A session found for the ticket must be in `implement` or `ship`. Running `/ticket-ship` is
  the person's consent to commit, push and open the draft PR, which an `implement` session never
  does on its own. A session in `plan` is refused with exit 4: nothing has been built. The recorded
  mode is left as it is (see *The recorded mode*).
- **Output.** The PR URL, alone, as the last line.
- **Exit codes.** The same as the scripts. `/ticket-start {id} ship` passes them through unchanged.

| Exit | Means |
|---|---|
| 0 | Done |
| 1 | An unexpected error |
| 2 | Busy or conflicting |
| 3 | The `az` or `gh` login has expired |
| 4 | Refused input: a ticket this repository cannot read or link (`workitem`'s `error`), a session in `plan` mode, a PR description over the host's limit |

## The recorded mode

`session-workflow`'s `session.ps1` has no verb that changes a live session's mode, and this skill
adds none. It leaves the mode recorded in the session file as it is. Rewriting it with
`session-start -Replace` would end the session first, and `session-end` releases every lock the
session holds, in the middle of the work those locks protect. The session ends at this skill's last
step in any case, so the recorded mode would outlive the rewrite by minutes. The consent to commit
and push comes from the invocation, not from the file.

## Steps

Run the script verbs as under *Script reference*. Call `session-step` as each step begins when there
is a session.

1. **Find the session and the worktree.**
   - Run `repo`. Exit 3: follow *Expired login*. Exit 4: `origin` is on neither host.
   - Run `workitem -Id {ticket}`, with `-Tracker` and `-AdoOrg` as `ticket-start`'s *Where the
     ticket lives* says, before anything else touches a session. It applies every tracker check: an
     issue in another repository, a GitHub issue on an Azure DevOps repository and a work item in
     another organization all stop here with exit 4. Keep its `tracker`, `id` and `url` for the
     draft PR.
   - Run `session-find -Id {ticket} -Repo {repo}`. It returns the ticket's session (by its subject
     `ticket:{id}`, or the `ticket` field of an older file) or `null`. Exit 4 with `mode-refused`: report
     that the ticket's session is in `plan` mode and stop. Do not end that session; it belongs to
     whoever started it.
   - With a session, work in its `worktree`. Without one, work in the ticket's worktree, found with
     `git worktree list` by its branch, or in the current directory if its branch belongs to the
     ticket. If none of these exists, stop and say so.
   - Without a session, start one with `session-workflow start`, subject `ticket:{id}` and mode
     `ship`, so the rest of the run holds locks and ends like any other session.
2. **Verify.** Run the verification the plan and local guidance name, with
   `superpowers:verification-before-completion`. Report build, standards check and tests separately,
   with actual outcomes. Include `pr-create`'s dependency audit (its step 4), so an advisory is
   fixed before the commit rather than found after the push. If verification fails, stop: no
   commit, no push. When `ship` delegated here
   straight after its own step 6, that verification stands unless a file has changed since.
   - **When the caller has staged a set that differs from the working tree** (`git diff --quiet`
     exits non-zero), verify what will be committed, not the working tree. Run
     `git worktree add --detach {tmp} HEAD`, apply the staged set there with
     `git diff --cached --binary | git -C {tmp} apply --index`, run the verification in `{tmp}`, then
     `git worktree remove --force {tmp}`. Never stash, unstage or restage the caller's files to do
     this.
3. **Commit.**
   - If the caller has staged changes, commit exactly the staged set and report what was left
     unstaged. Never unstage.
   - If nothing is staged, commit the ticket's changes: everything on the ticket branch belongs to the
     ticket. Run `git status --short` first, and confirm no unexpected or ignored evidence files would
     go in.
   - **Complete the plan at the PR**, as `plan-workflow` describes, unless it is already complete.
     When the plan is already in `planning/completed/` with its `## Status` section, as it is after
     `ticket-start ship` step 6, leave its Status section, `status:` and `completed:` as they are;
     add only reminders that are not yet listed. Otherwise development done and going to a
     PR means the plan completes now: append its `## Status` section, set `status: completed` and
     `completed:`, and move it to `planning/completed/`. Each check that can only happen after the PR
     (a deployment, a real run, a sign-off) becomes a `verify` reminder through `reminders-set`,
     listed under the plan's `### Reminders Set`. The reminders go in the same commit.
   - The completed plan is one of the ticket's changes: commit it with the code, together with the
     removal of any copy of it committed earlier in another column. A commit whose plan is not in
     `planning/completed/` is unfinished. A staged set from the caller that leaves the plan out is
     committed as staged, and the report says the plan was left out. Where local guidance says plans
     are not committed, follow it and say so.
   - Check the board with `plan-doc.cs validate --path planning`.
   - Use the local commit message convention.
4. **Push.** `git push -u origin {branch}`. Never force-push. A refused push stops the run with the
   commit kept locally.
5. **Draft PR.**
   - Write the description to a file following `references/pr-description.md` and count it.
   - Open the PR with `pr-create`: source the ticket branch, target the server's `defaultBranch`
     from `repo` unless local guidance names another, as a draft, with the ticket linked. Tell
     `pr-create` the `tracker` that step 1's `workitem` returned: on Azure DevOps it links work item `{id}`, and on
     GitHub it writes `Refs #{id}` for an issue or `AB#{id}` for an Azure Boards work item.
     `pr-create` returns an existing open PR from the branch instead of opening a second.
   - Never merge, set auto-complete or add reviewers. Never edit the ticket.
6. **Finish.** Run `session-workflow end`: it removes the session's isolated environment, then runs
   `session-end`, which releases every lock the session holds and removes its file. The worktree
   stays for review; removing it after the PR merges is the caller's decision. End with the PR URL,
   alone, as the last line.

## Failures

Every stop ends the session through `session-workflow end`. Holding a session open after a failure
leaves locks and isolated environments behind. The worktree, the branch and the remote survive the
stop, and each step starts from what it finds, so a rerun picks up where the failure stopped.

| Fails at | What happens | The rerun |
|---|---|---|
| Verify | Stop. No commit, no push. The session ends | Verifies again |
| Commit | Stop. The work stays uncommitted in the worktree. The session ends | Commits |
| Push (refused, or exit 3) | Stop. The commit stays local. The session ends | Pushes, never with force |
| Draft PR (exit 3 or 4) | Stop. The branch stays pushed. The session ends | `pr-create` returns the existing PR or opens one |
| `session-workflow end` | Report it. `sessions` shows the leftover, which a person breaks with agreement | Ends the session |

## Expired login

A script verb that reaches a server exits 3 when its `az` or `gh` login is missing or expired. Its
JSON carries `loginCommand`, and for `az` a `tenant` when the Azure CLI still has one cached. On
exit 3:

- Stop immediately. Do not retry and do not run the login command yourself; it is interactive.
- Give the person `loginCommand` as it is. For `az` without a `tenant`, ask the person for their
  tenant, then give them `az login --tenant {their answer}`.
- Name the step that stopped and what is already done. The next invocation resumes there.
- Run `session-workflow end`, as at any other stop.

## Script reference

`ticket.ps1` is `ticket-start`'s script; `session.ps1` is `session-workflow`'s.

```powershell
$tk = if ($env:CLAUDE_PLUGIN_ROOT) {
  "$env:CLAUDE_PLUGIN_ROOT\skills\ticket-start\scripts"
} else {
  "$HOME\.claude\skills\ticket-start\scripts"
}
& "$tk\ticket.ps1" session-find -Id 22744 -Repo Contoso.Orders
```

| Verb | Does |
|---|---|
| `repo` | Reads `origin`: the `host`, the repository and the server's `defaultBranch` |
| `workitem -Id [-Tracker] [-AdoOrg]` | Reads and checks the ticket; its `tracker`, `id` and `url` |
| `session-find -Id -Repo` | The ticket's session in this repository, or `null`. Exit 4 (`mode-refused`) for a session in a mode other than `implement` or `ship` |
| `pr-find -Branch` | Open PRs from the branch |

The draft PR is `pr-create`'s; `ticket.ps1` has no verb for it.

## Do not

- Force-push, merge, set auto-complete or add reviewers.
- Edit the ticket: no field, state, label or comment, on either tracker.
- Run `az login` or `gh auth login`.
- Stash, discard or unstage anyone's work.
- Change a live session's recorded mode, or end a session this skill refused.
- Hold a session open after a failure.
