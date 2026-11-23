# Dependency audit: .NET

**Markers:** a `*.sln`, `*.slnx` or `*.csproj` in the repository.

## What the build already enforces

Read the audit settings before running anything. They live in `Directory.Build.props`, `Directory.Packages.props` or the project files:

| Property | Means |
|-|-|
| `NuGetAudit` | `true` by default on the .NET 8 SDK and later; `false` turns the audit off |
| `NuGetAuditMode` | `direct` (the default before .NET 10) or `all`, which includes transitive packages |
| `NuGetAuditLevel` | The lowest severity reported: `low`, `moderate`, `high` or `critical` |
| `TreatWarningsAsErrors`, or `WarningsAsErrors` listing `NU1901`–`NU1904` | Makes an advisory fail the build |

Where the repository treats audit warnings as errors, `dotnet restore` is the pipeline's own gate,
reproduced exactly. Where it does not, the advisories are warnings, and the second command below is
what reports them.

## Commands

```bash
dotnet restore                                   # must exit 0
dotnet list package --vulnerable --no-restore    # must report no vulnerable packages
```

`dotnet restore` names the package and the advisory URL on failure. The second command turns a pass
into a readable per-project confirmation.

Leave out `--include-transitive` unless `NuGetAuditMode` is `all`. Under `direct`, a transitive
advisory is not what the build gates on, and including them turns a clean pass into moderate findings
in packages you cannot pin from here. Audit transitives on their own schedule.

## Do not

- **Pass `-p:NuGetAudit=false`** to get either command to succeed. That flag is for working offline,
  where the audit cannot run at all; using it here hides the thing you are checking for.
- Add a `NuGetAuditSuppress` entry to make the PR pass. A suppression is a decision for the
  repository's owners, with its reason recorded; propose it in the report instead.

## A finding

`NU1901`–`NU1904` (low to critical) is the finding. Fix it by raising the package version, or for a
transitive package under `NuGetAuditMode` `all`, by pinning the fixed version in
`Directory.Packages.props` or the project. Then run both commands again.
