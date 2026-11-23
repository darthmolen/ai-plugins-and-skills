# Draft PR description

ADO caps a PR description at 4000 characters, and `ado-pr` rejects a description it has not counted.
Write it to a file, count it as `ado-pr` describes, then open the PR.

Follow local guidance on the PR title and description. Where it is silent:

- **Title.** The repository's recent PR titles show the convention. Some repositories use the
  ticket number then a summary; others use a prefix such as `[FEATURE]`.
- **Summary.** What changed and why, in a few sentences.
- **Ticket.** The ticket number and URL. When the repository's project differs from the ticket's
  `teamProject`, say so here; the link still works within the organization.
- **Verification.** Build, standards check and tests as separate lines, with actual outcomes.
  Include end-to-end results or the reason none applied.
- **Reminders.** The `verify` reminders the PR leaves for checks that come after it.
- **Not done.** Anything skipped, blocked or deferred, and any suppression with its justification.

Keep it plain text or the Markdown the repository's PRs already use. Count the characters before
sending; trim the summary before the verification.
