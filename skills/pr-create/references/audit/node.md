# Dependency audit: Node

**Markers:** `package.json`. Angular, React and any other npm-based frontend or service audit the
same way; the framework does not change the command. The lockfile says which package manager:

| Lockfile | Package manager |
|-|-|
| `package-lock.json` or `npm-shrinkwrap.json` | npm |
| `pnpm-lock.yaml` | pnpm |
| `yarn.lock` with a `.yarnrc.yml` beside it | Yarn 2 or later |
| `yarn.lock` with no `.yarnrc.yml` | Yarn 1 |

A `packageManager` field in `package.json` settles any doubt. With no lockfile, there is nothing
pinned to audit; say so in the report.

The audit covers the packages that ship, not the development dependencies, and fails at `high` or
above, unless the pipeline sets another level.

## Commands

| Package manager | Command | Fails when |
|-|-|-|
| npm | `npm audit --omit=dev --audit-level=high` | Exit code is not 0 |
| pnpm | `pnpm audit --prod --audit-level high` | Exit code is not 0 |
| Yarn 2 or later | `yarn npm audit --all --recursive --environment production --severity high` | Exit code is not 0 |
| Yarn 1 | `yarn audit --groups dependencies` | Exit code is 8 or more |

- npm and pnpm audit transitive packages as well as direct ones. Yarn 2 or later audits direct
  dependencies only unless given `--recursive`.
- Yarn 1's exit code is a sum of flags for the severities found: 1 info, 2 low, 4 moderate, 8 high,
  16 critical. `--level` filters what is printed, so judge by the exit code: 8 or more means a high
  or critical advisory.
- In a workspace (monorepo), run the command once at the root. npm, pnpm and Yarn 1 cover every
  workspace from there; Yarn 2 or later covers only the current workspace without `--all`, which the
  command above passes.

## Do not

- Run `npm audit fix --force`. It installs major versions outside the declared ranges, which is a
  dependency upgrade, not an audit.
- Pass `--ignore` (pnpm, Yarn 2 or later) or add an audit exception to the repository's config to
  make the PR pass. An ignored advisory is a decision for the repository's owners, with its reason
  recorded; propose it in the report instead.

## A finding

For a direct dependency, raise its version in `package.json` and reinstall. For a transitive one,
`npm audit fix` (without `--force`) raises it within the declared ranges where a fixed version
exists. Where none fits the ranges, pin it with the package manager's override (`overrides` for
npm, `pnpm.overrides`, `resolutions` for Yarn), and say in the PR's caveats that the override was
added. Then run the audit again.
