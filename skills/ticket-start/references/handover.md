# Handover report

`implement` ends with this report. The changes stay unstaged and uncommitted in the ticket's
worktree.

1. **Where.** The worktree path, the branch, and the plan document's path. The plan is in
   `planning/completed/` when everything passed, and anywhere else only with the reason.
2. **What changed.** Files grouped by purpose, from `git status --short`. Confirm nothing is staged
   and the branch has no commits ahead of the base.
3. **Build.** The command run and its actual outcome.
4. **Standards check.** The format and analyzer command and its outcome.
5. **Tests.** Each suite, the command, and the result. Report build, standards and tests separately.
6. **End-to-end.** What ran, or the reason none applied.
7. **Suppressions.** Each one and its justification.
8. **Blocked or skipped.** Each check not run and why, including any lock that could not be taken,
   any isolated environment that could not be created, and any test blocked because it needs a real
   external service the isolated environment cannot reach.
9. **Environment actions.** Anything installed, started, reset or restored, and what was left
   running.
10. **Assumptions.** Each one made because local guidance was silent or the run was unattended, with
    the more conservative reading that was taken.
11. **Next.** To ship, invoke `/ticket-start {id} ship`. It commits, pushes and opens the draft PR.
