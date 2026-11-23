# GitHub issues

A ticket in GitHub Issues is an issue in this repository. `workitem` reads it with the GitHub
CLI's login. An issue in another repository is refused (`other-repository`): start the ticket from
the repository that holds it.

## Reading it

| Field | Holds |
|---|---|
| `type` | The issue type where the organization has set them up (`Bug`, `Feature`, `Task`). Otherwise read from the labels: a `bug` label gives `Bug`, an `enhancement` or `feature` label gives `Feature`. Anything else is empty, and the branch name is asked for |
| `description` | The issue body, in Markdown |
| `acceptanceCriteria`, `reproSteps` | Always empty. Issues have no such fields; look for them as sections or checklists in the body, and flag them as missing when the body has none |
| `state` | `open` or `closed`. A closed issue is worth confirming with the caller before planning it |
| `labels`, `milestone` | As set on the issue |
| `relations` | Always empty |

A number that is a pull request, not an issue, is refused (`is-pull-request`): the two share one
number space.

## Linking

`pr-create` writes `Refs #{id}` in the PR description by default. `Closes #{id}`, which closes the
issue when the PR merges, is written only when the caller asks for it.

## Expired login

`workitem`, `repo` and `pr-find` exit 3 when `gh` has no login (it exits 4 and asks for
`gh auth login`) or GitHub refuses its token (`HTTP 401`). The JSON carries
`loginCommand: "gh auth login"`; give it to the caller.

## Do not

- Run `gh issue edit`, `gh issue comment`, `gh issue close` or anything else that changes the issue,
  its labels, assignees or milestone.
