# How `close` moves a document

The part of `plan-workflow`'s *The scripts* that only matters when `close` refuses, or when you
change `plan-doc.cs`.

## What `close` refuses

Each refusal is a document that would otherwise lie about itself:

- a status whose required field is missing (`completed` without `completed:`, `blocked` without
  `blocked_on:`, `not-implemented` without `reason:` **and** the field that reason must name)
- a `--decided-on` that is not ISO `YYYY-MM-DD`
- a `--reason-target` shaped like a path rather than a name
- a document that already carries a `## Status` section — it would append a second
- a document whose `status:` and directory **already** disagree; something upstream is broken and
  this is not the tool to paper over it

## The `git add` that looks redundant and is not

`close` runs `git add` on the destination after `git mv`. That is load-bearing: **`git mv` stages
the rename from the blob already in the index**, so the frontmatter rewrite — a working-tree change
never added — is left unstaged, and the commit moves the file while committing its *old* content.
Observed for real: a document landed in `completed/` still saying `status: in-progress`, which is
the precise drift this script exists to prevent, shipped by the script's own move.

That line is covered by `../test/Invoke-CloseIntegrationCheck.ps1`, not by `--selftest`. It is a property
of the git index, which no pure battery reaches — and a mutant deleting the `git add` **survived**
the battery, which under `test-filter-development` rejects the test rather than the mutant.

```powershell
.\test\Invoke-CloseIntegrationCheck.ps1     # 24 checks against a throwaway repo, exit 0 required
```

PowerShell 7, matching the other PowerShell checks in this collection — not bash, so it runs on Windows without Git Bash and on Linux and macOS alike.
