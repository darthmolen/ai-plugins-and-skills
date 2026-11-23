---
name: pr-create
description: Use when opening, or updating the description of, a pull request on Azure DevOps or GitHub — after plan-workflow's close, from ticket-ship, for a wave lead, as a draft with work items or issues linked — including when the branch may already have an open PR, or a create call has already been rejected for a too-long description. Detects the host from the remote and follows its reference.
metadata:
  category: tools
---

# Creating Pull Requests

**Where this fits.** This is the last step of the pipeline the planning and review skills set up.
`plan-workflow`'s close ends with "push and raise the PR", `code-send-review` stops at "now raise
the PR", `ticket-ship` opens its draft here, and the wave lead opens its PRs here. Every workflow in
this plugin that ends in a pull request names this skill, whatever the host.

This file covers what is the same on every host. The calls, limits and link syntax of each host are
in its reference:

| Host | Reference | Tools |
|-|-|-|
| Azure DevOps | [references/ado.md](references/ado.md) | The `ado` MCP tools |
| GitHub | [references/github.md](references/github.md) | The `gh` CLI |

## 1. Detect the host

Read it from the remote; do not assume it from the repository you last worked in.

```bash
git remote get-url origin
```

| The URL contains | Host |
|-|-|
| `dev.azure.com`, `visualstudio.com` (HTTPS or SSH) | Azure DevOps |
| `github.com` | GitHub |
| Anything else | Ask, unless local guidance (`CLAUDE.md`, `AGENTS.md`) names the host |

Then read the host's reference and follow it for every call below.

## 2. Confirm the branch is on the remote

An unpushed source branch is the other way a create call fails after the description is done.

```bash
git branch --show-current
git rev-parse --verify origin/<branch>     # the source branch must exist on the remote
git rev-parse --verify origin/<target>     # usually origin/main
```

The target is the branch the caller names, else the one local guidance names, else the
repository's default branch as the host reports it.

## 3. Return an existing PR instead of opening a second

Before creating, list the open PRs from the source branch, as the host's reference shows. If there
is one, return it: its number, URL and draft state. Do not create another. A second run on the same
branch then ends at the same PR, which is what lets `ticket-ship` and the wave lead rerun after a
failure.

If the caller asked for a changed description, update that PR's description instead, counted as
in step 6.

## 4. Dependency audit before you open it

Why this belongs here: an advisory published after your last green build breaks the next run with
nothing in your diff to explain it. Catching it now costs one command; catching it at publish costs
a release number, because the version counter moves per run whether the run succeeds or not. Build,
tests and lint are the caller's verification, done before the commit; this step is only the audit.

The aim is to run locally the audit the pipeline will run, so find the pipeline's first.

**a. Read the pipeline.** Look in `azure-pipelines*.yml`, `.azure-pipelines/`, `.pipelines/` and
`.github/workflows/*.yml`, including the templates they reference, for an audit step:

```bash
grep -rniE "npm audit|pnpm audit|yarn (npm )?audit|pip-audit|uv audit|vulnerable|NuGetAudit" \
  azure-pipelines*.yml .azure-pipelines .pipelines .github/workflows 2>/dev/null
```

Where it runs one, run that same command, with its level and scope, from the folder it runs in.
Where the pipeline's tool cannot run here (it needs a service token, such as Snyk, or it is a
server-side check, such as GitHub's dependency review), use the default for the stack and say in
the report which check the pipeline will still run.

**b. Find the stacks.** Where the pipeline has no audit step, the files in the repository say which
stacks it has. Check the root and the folders the pipeline builds; a repository often has more than
one (a .NET API with an Angular frontend audits both).

| Marker | Stack | Reference |
|-|-|-|
| `*.sln`, `*.slnx`, `*.csproj` | .NET | [references/audit/dotnet.md](references/audit/dotnet.md) |
| `pyproject.toml`, `uv.lock`, `requirements*.txt`, `pylock.toml` | Python | [references/audit/python.md](references/audit/python.md) |
| `package.json` (Angular, React and any other npm project) | Node | [references/audit/node.md](references/audit/node.md) |

```bash
git ls-files '*.sln' '*.slnx' '*.csproj' '*pyproject.toml' '*uv.lock' '*requirements*.txt' \
  '*pylock.toml' '*package.json' | grep -v node_modules
```

**c. Run each stack's audit** as its reference describes, and fix what it finds before creating the
PR. A finding is a package to raise, not an obstacle: no reference allows turning the audit off or
ignoring an advisory to get the PR open.

**d. Report** which audits ran and their results in the description's Verification section. When a
stack has no reference here, or nothing pinned to audit, say so there rather than skipping the step
silently.

## 5. Title

Follow the repository's own convention. One repository titles its PRs with the commit prefix,
`[FEATURE] <summary>` or `[FIX] <summary>`, matching its `CLAUDE.md` commit format. Another leads
with the release version, e.g. `4.5.8 - OrderContext.IsRetry`. Look at a few recently merged PRs in
the target repository; do not carry one repository's style into another.

## 6. Description

### Write it to a file and count it

Every host caps the description, and a rejected call re-sends the whole body. Write the description
to a file, count its characters against the host's limit, and only then make the call.

```bash
cat > "$SCRATCH/pr.md" <<'EOF'
<description body>
EOF
python3 -c "print(len(open(r'$SCRATCH/pr.md',encoding='utf-8').read().rstrip('\n')))"
```

Count characters, not bytes. `wc -c` reports bytes, and an em dash, smart quote or arrow is 3 bytes
to 1 character, so it over-counts prose. The Python line decodes UTF-8 and strips the heredoc's
trailing newline.

The limit is in the host's reference. On Azure DevOps it is 4000 characters and is the reason this
skill counts at all; on GitHub it is far larger, but the count is still one line and the same body
may be moved to Azure DevOps later.

### What belongs in it

The PR body is reviewer guidance, not a second copy of the commits. Writing it as a summary of
every commit is what pushes it over the limit.

Include:
- **Why** this exists: the problem, in a few sentences
- **What changed**, one short paragraph per area
- **Decisions worth reviewing**: the two or three judgement calls a reviewer should push back on
- **Verification**: build, format, test counts, as results not narrative
- **Caveats** the reviewer must know (version numbers, migration order, follow-ups)
- **Links** to the work items or issues, in the host's syntax, when the host links them from the
  body (see the reference)

Leave out, because it already lives in committed content:
- Reasoning that is already in the commit messages
- File-by-file change lists; the diff shows those
- Full lesson and deviation write-ups; those belong in the plan document

### When it overflows

Cut in this order, so the reviewer keeps what they need:

1. Anything duplicating a commit message or the changelog
2. Incidental fixes made in passing
3. Package and dependency notes
4. Long verification prose, collapsed to one line of results

**Never cut** the decisions-worth-reviewing section, the caveats or the links. Those are the parts a
reviewer cannot reconstruct from the diff.

An incidental fix that changes behaviour outside the feature's blast radius (a pre-existing bug
fixed in passing) is a caveat, not an incidental: keep it, compressed.

If something load-bearing has to go, say so in the response. A dropped section that nobody knows
was dropped is worse than a long PR.

## 7. Draft and links

- **Draft.** Open the PR as a draft when the caller asks for one: a ticket's PR, or any PR a person
  reviews before it is published. Both hosts default to a published PR.
- **Links.** The caller passes the work item or issue IDs. A ticket's PR links its ticket; a plan's
  PR links a work item when the plan names one. How the link is made depends on the host and on
  where the tickets live, which can differ: a GitHub repository may track its work in Azure Boards.
  The reference for the host has the syntax for each case.

## 8. Report

Report the PR number, the URL, whether it is a draft, whether it was created or already existed, and
anything trimmed out of the description, so the author can decide whether it goes back in.

## Red flags: stop and count

- "That should be under the limit now"
- "I only cut a paragraph, close enough"
- A second create call after a too-long rejection, without a count in between
- Editing the description inline in the tool call rather than in the file
- A counted body within ten characters of the limit and the thought "that's basically fine"
- Several polishing passes shaving ~150 characters each against an overshoot of 1000 or more: make a
  structural cut instead

## Do not

- Merge, set auto-complete or add reviewers, unless the caller asks.
- Open a second PR from a branch that already has an open one.
- Run an interactive login (`az login`, `gh auth login`). Stop and give the person the command.
- Edit the linked work item or issue.
