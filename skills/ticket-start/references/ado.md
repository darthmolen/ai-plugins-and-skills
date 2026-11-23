# Azure Boards work items

A ticket in Azure Boards is a work item. `workitem` reads it with the Azure CLI's login, from the
organization `origin` names, or for a GitHub repository from `-AdoOrg` or the work item URL.

## Reading it

| Field | Holds |
|---|---|
| `type` | The work item type: `User Story`, `Bug`, `Task`, `Feature` and so on. `branch-name` has a fallback only for `User Story`, `Feature` and `Bug` |
| `description`, `acceptanceCriteria`, `reproSteps` | HTML. Read them as text. A Bug usually keeps its steps in `reproSteps`; a User Story its criteria in `acceptanceCriteria` |
| `state` | The board column's state, such as `New`, `Active` or `Resolved` |
| `teamProject` | The project the work item belongs to. When it differs from the repository's project, the PR description says so; the link still works within the organization |
| `iterationPath` | The sprint |
| `relations` | Parent, child and related links, with their names |

Work item IDs are unique across an organization, so the number alone finds it.

## Linking

On an Azure DevOps repository, `pr-create` links the work item through the PR's work item
argument. On a GitHub repository, it writes `AB#{id}` in the PR description, which resolves where
the Azure Boards app is connected to the repository.

## Expired login

`workitem`, `repo` and `pr-find` exit 3 when the Azure CLI cannot get a token, its output mentions
`az login`, `AADSTS` or a refresh token, Azure DevOps answers 401 or 203, or it returns an HTML page
where JSON was expected. The JSON carries `loginCommand`, and `tenant` when the Azure CLI still has
one cached. Without a cached tenant, ask the caller for theirs and give them
`az login --tenant {their answer}`.

## Do not

- Run `az boards` or `az repos`. They need an extension that may be missing, and its install prompt
  hangs a non-interactive shell.
- Change the work item's state, fields, tags or discussion.
