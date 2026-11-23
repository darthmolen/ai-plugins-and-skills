---
name: ado-pr
description: Use when creating or updating an Azure DevOps pull request via the ado MCP tools — after plan-workflow's close, for a wave lead, as a draft with work items linked — including when the branch may already have an active PR, or a create call has already been rejected for a too-long description
metadata:
  category: tools
---

# Azure DevOps Pull Requests

**Where this fits.** This is the last step of the pipeline the planning and review skills set up.
`plan-workflow`'s close ends with "push and raise the PR", `code-send-review` stops at "now raise
the PR", and the wave lead opens its PRs here. Use it whenever a workflow in this plugin ends in a
pull request on Azure DevOps; on GitHub, `gh pr create` takes its place.

## Overview

`mcp__ado__repo_pull_request_write` rejects a `description` over **4000 characters** with a validation error and no partial success. The body is re-sent on every retry, so guessing at a trim burns the whole payload each time.

**Core rule: never submit a description you have not counted.**

## The Iron Rule

Write the description to a file, count it, then submit.

```bash
cat > "$SCRATCH/pr.md" <<'EOF'
<description body>
EOF
python3 -c "print(len(open(r'$SCRATCH/pr.md',encoding='utf-8').read().rstrip('\n')))"
```

Only after the count is under budget do you call the tool.

**Count characters, not bytes.** The limit is 4000 *characters*; `wc -c` reports *bytes*, and every em dash, smart quote or arrow is 3 bytes to 1 character. `wc -c` therefore over-counts a prose body and will push you into cuts you did not need. Use the Python line above — it decodes UTF-8 and strips the heredoc's trailing newline, which `wc` also counts but the `description` argument does not need. `wc -m` works too if the locale is UTF-8, but is not worth verifying. Do **not** try to prove a body is ASCII with `grep '[^\x00-\x7F]'` — inside a POSIX bracket expression those backslashes are literal, so it matches `\`, `x`, `0`-`7` and `F` and reports false positives.

**No exceptions:**
- Not when it "looks about right"
- Not when you already trimmed once
- Not when you cut a whole section and are sure it fits now
- Not for an `update` that edits an existing description

Trimming by eye and resubmitting is the failure this skill exists to prevent. Three rejected calls in a row is the observed baseline.

## Budget

| Limit | Value |
|-|-|
| Hard cap (`description`) | 4000 characters — enforced, no partial success |
| Working target | **~3500**, when the required content fits in it |

Markdown, code fences and tables all count; a table costs more than it looks.

**3500 is an aim, not a third rule.** On a change with several genuine decisions, the required sections plus the never-cut sections can legitimately exceed 3500. When they do, land under 4000 with whatever headroom you can and **say in your response that you missed the target and why** — do not sacrifice a protected section to hit a soft number. Anything at 3990+ is worth one more pass anyway: a later edit to the description has no room.

## Required arguments

```
action: "create"
repositoryId, project     # project is REQUIRED when repositoryId is a name, not a GUID
sourceRefName: "refs/heads/<branch>"    # full ref, not a bare branch name
targetRefName: "refs/heads/main"        # or the integration branch the plan names
title
description
isDraft: true             # when the caller asks for a draft
workItems: "<id> <id>"    # space-separated work item IDs, when there are any
```

**Draft.** Pass `isDraft: true` when the caller asks for a draft: a ticket's PR, or any PR a person
reviews before it is published. The create call defaults to `false`.

**Work-item links.** Pass the work item IDs in `workItems`, space-separated. A ticket's PR links its
ticket. A plan's PR links a work item when the plan names one. Leave the argument out when there is
none.

**The title follows the repository's own convention — read, don't assume.** One repository
titles its PRs with the commit prefix, `[FEATURE] <summary>` or `[FIX] <summary>`, matching its
`CLAUDE.md` commit format. Another leads with the release version, e.g.
`4.5.8 - OrderContext.IsRetry`. Match the target repo by looking at a few of its recently
merged PRs; do not carry one repo's style into another.

Read repo and project from the remote rather than guessing, and confirm the source branch is actually pushed — an unpushed `sourceRefName` is the other way this call gets rejected after you have done all the counting work:

```bash
git remote -v                                        # .../<org>/<project>/_git/<repo>
git branch --show-current
git rev-parse --verify origin/<branch>               # source ref must exist on the remote
git rev-parse --verify origin/<target>               # usually origin/main
```

Project and repo names frequently **collide** (`.../Contoso.Orders/_git/Contoso.Orders`). That is correct, not a copy-paste error — pass the same string for both.

## Return an existing PR instead of opening a second

Before the create call, list the active PRs from the source branch:

```
mcp__ado__repo_pull_request
  action: "list"
  repositoryId, project
  sourceRefName: "refs/heads/<branch>"
  status: "Active"
```

If the list has a PR, return the first one: its ID, URL and draft state. Do not call `create`. A
second run on the same branch then ends at the same PR. If the description needs changing, edit that PR with `action: "update"` and count the
new description first, as below.

If the list is empty, create the PR.

## Dependency audit before you open it

On a .NET repo, run these two before creating the PR:

```bash
dotnet restore                                   # must exit 0
dotnet list package --vulnerable --no-restore    # must report no vulnerable packages
```

`dotnet restore` is the whole check where the repo sets `NuGetAudit` with `TreatWarningsAsErrors` — it reproduces the publish pipeline's audit gate exactly and names the package and advisory URL on failure. The second command turns a pass into a readable per-project confirmation.

**Do not pass `-p:NuGetAudit=false` to get either of them to succeed.** That flag is for working offline, where the audit cannot run at all; using it here hides the thing you are checking for. If `dotnet restore` reports `NU1903`, fix the package pin — that is the finding, not an obstacle.

Deliberately **without** `--include-transitive`: audit mode is usually `direct`, so a transitive advisory is not what the build gates on, and including them turns a clean pass into a wall of moderate findings in packages you cannot pin from here. Audit transitives on their own schedule, not in a PR pre-flight.

Why this belongs here: an advisory published *after* your last green build breaks the next run with nothing in your diff to explain it. Catching it now costs one restore; catching it at publish costs a release number, because the version counter moves per run whether the run succeeds or not.

## What belongs in the body

The PR body is **reviewer guidance**, not a second copy of the commits. Writing it as a summary of every commit is what pushes it over 4000.

Include:
- **Why** this exists — the problem, in a few sentences
- **What changed**, one short paragraph per area
- **Decisions worth reviewing** — the two or three judgement calls a reviewer should push back on
- **Verification** — build, format, test counts, as results not narrative
- Caveats the reviewer must know (version numbers, migration order, follow-ups)

Leave out, because it already lives in committed content:
- Reasoning that is already in the commit messages
- File-by-file change lists — the diff shows those
- Full lesson/deviation write-ups — those belong in the plan document

## When it overflows anyway

Cut in this order, so the reviewer keeps what they need:

1. Anything duplicating a commit message or the changelog
2. Incidental fixes made in passing
3. Package/dependency notes
4. Long verification prose → collapse to one line of results

**Never cut** the decisions-worth-reviewing section or the caveats. Those are the only parts a reviewer cannot reconstruct from the diff.

Cut order #2 and the never-cut rule overlap when an incidental fix **changes behaviour outside the feature's blast radius** — a pre-existing bug fixed in passing, say. Keep those, compressed: they belong to caveats, not to incidentals, precisely because the feature's own diff does not explain them.

If something load-bearing has to go, say so in the response — a dropped section that nobody knows was dropped is worse than a long PR.

## Red flags — stop and count

- "That should be under the limit now"
- "I only cut a paragraph, close enough"
- Second `create` call after a `too_big` rejection, without a count in between
- Editing the description inline in the tool call rather than in a file
- **A counted body at 4000-4010 and the thought "that's basically fine"** — it is not, the cap is exact and rejection costs the whole payload
- Two or three polishing passes shaving ~150 characters each against an overshoot of 1000+ — stop and make a structural cut instead

All of these mean: write it to a file and count the characters.

## After creating

Report the PR id, the URL, whether it is a draft, whether it was created or already existed, and **anything trimmed out**, so the author can decide whether it needs to go back in.

URL shape: `https://dev.azure.com/<org>/<project>/_git/<repo>/pullrequest/<id>`
