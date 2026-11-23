# Azure DevOps

Pull requests on Azure DevOps go through the `ado` MCP tools. Follow `SKILL.md` for the steps; this
file has the calls and the limits.

## Limits

| Limit | Value |
|-|-|
| Hard cap (`description`) | 4000 characters, enforced, no partial success |
| Working target | About 3500, when the required content fits in it |

`mcp__ado__repo_pull_request_write` rejects a description over 4000 characters with a validation
error, and the retry re-sends the whole body. Three rejected calls in a row is the observed baseline
when the body is trimmed by eye. Count it as `SKILL.md` step 6 shows, every time:

- Not when it "looks about right"
- Not when you already trimmed once
- Not when you cut a whole section and are sure it fits now
- Not for an `update` that edits an existing description

Markdown, code fences and tables all count; a table costs more than it looks.

3500 is an aim, not a second limit. On a change with several genuine decisions, the required
sections can exceed it. Then land under 4000 with whatever headroom you can and say in your response
that you missed the target and why. Do not sacrifice a never-cut section to hit it. A body at 3990
or more is worth one more pass anyway: a later edit to the description has no room.

## Repository and project

Read them from the remote:

```
https://dev.azure.com/<org>/<project>/_git/<repo>
<org>@vs-ssh.visualstudio.com:v3/<org>/<project>/<repo>
git@ssh.dev.azure.com:v3/<org>/<project>/<repo>
```

Project and repository names often collide (`.../Contoso.Orders/_git/Contoso.Orders`). That is
correct, not a copy-paste error: pass the same string for both.

## Existing PR

```
mcp__ado__repo_pull_request
  action: "list"
  repositoryId, project
  sourceRefName: "refs/heads/<branch>"
  status: "Active"
```

## Create

```
mcp__ado__repo_pull_request_write
  action: "create"
  repositoryId, project      # project is required when repositoryId is a name, not a GUID
  sourceRefName: "refs/heads/<branch>"     # full ref, not a bare branch name
  targetRefName: "refs/heads/main"         # or the target the caller names
  title
  description
  isDraft: true              # when the caller asks for a draft; defaults to false
  workItems: "<id> <id>"     # space-separated work item IDs, when there are any
```

To change an existing PR's description, use `action: "update"` with the PR's ID and the counted
description.

## Links

Work items link through the `workItems` argument, not through the body. Leave the argument out when
there are none. A work item from another project in the same organization links the same way; say in
the description that the projects differ.

## Expired login

When a call fails with an authentication error (`AADSTS`, 401, a token or `az login` message), stop.
Do not run `az login`, which is interactive. Give the person `az login --tenant <tenant>`, asking
for the tenant when `az account show --query tenantId -o tsv` has none cached.

## URL

`https://dev.azure.com/<org>/<project>/_git/<repo>/pullrequest/<id>`
