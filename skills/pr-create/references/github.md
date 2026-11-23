# GitHub

Pull requests on GitHub go through the `gh` CLI. Follow `SKILL.md` for the steps; this file has the
commands and the limits.

## Limits

| Limit | Value |
|-|-|
| Hard cap (body) | 65536 characters |

The cap is rarely reached, but count the body as `SKILL.md` step 6 shows anyway. A body that would
not fit the Azure DevOps cap of 4000 is usually a body that repeats the commits, and the cut order
in `SKILL.md` applies at any length.

## The repository

`gh` does not always use `origin`: with an `upstream` remote it prefers `upstream`, and `GH_HOST`
can point it at a GitHub Enterprise server. Take the repository from `origin` once and pass it to
every command, host included:

```bash
R=$(git remote get-url origin | sed -E 's#^(https://([^@/]+@)?|(ssh://)?git@)github\.com[:/]##; s#\.git/?$##; s#/$##')
R="github.com/$R"                       # github.com/<owner>/<repo>
```

Every `gh pr` command below takes `--repo "$R"`, and `gh repo view` takes `"https://$R"`.

## Before the first call

```bash
gh auth status --hostname github.com
```

If it reports no login or an expired token, stop. Do not run `gh auth login`, which is interactive.
Give the person `gh auth login` (or `gh auth login --hostname <host>` for GitHub Enterprise) and
name the step that stopped.

The default branch, when the caller and local guidance name no target:

```bash
gh repo view "https://$R" --json defaultBranchRef --jq .defaultBranchRef.name
```

## Existing PR

```bash
gh pr list --repo "$R" --head <branch> --state open --json number,url,isDraft
```

## Create

Pass the body as a file, never inline, so the counted file is what is sent.

```bash
gh pr create \
  --repo "$R" \
  --base <target> \
  --head <branch> \
  --title "<title>" \
  --body-file "$SCRATCH/pr.md" \
  --draft                      # when the caller asks for a draft
```

`--base` and `--head` take bare branch names, not `refs/heads/...`. `gh pr create` prints the PR
URL as its last line.

To change an existing PR's description:

```bash
gh pr edit <number> --repo "$R" --body-file "$SCRATCH/pr.md"
```

## Links

GitHub has no argument for linked work; links go in the body, in a **Links** line. The syntax
depends on where the tickets live.

| Tickets live in | Write | Effect |
|-|-|-|
| GitHub Issues | `Refs #123` | Links the issue; nothing else happens |
| GitHub Issues, and the caller asks to close it on merge | `Closes #123` | Closes the issue when the PR merges into the default branch |
| Azure Boards | `AB#22744` | Links the work item, through the Azure Boards app installed on the repository |

Use `Refs` by default. A draft PR opened by an agent should not close an issue on merge unless the
caller asked for it, and `Closes` only acts when the PR targets the default branch. An issue in
another repository is `owner/repo#123`.

`AB#` links only work where the Azure Boards app is connected to the repository. If the
repository's merged PRs never use `AB#`, say in the report that the link may not resolve.

## URL

`https://github.com/<owner>/<repo>/pull/<number>`
