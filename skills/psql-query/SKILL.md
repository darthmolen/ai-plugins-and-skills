---
name: psql-query
description: Use when running ad-hoc read-only SQL against Azure Database for PostgreSQL — exploring tables, sampling rows, checking key uniqueness/overlap across tables, inspecting pg_stat_* / pg_catalog, or producing JSON/CSV for a pipeline. Read-only by construction (server-enforced read-only transaction + client-side guard, always rolled back; no INSERT/UPDATE/DELETE/DDL/COPY/dblink). Returns TOON by default for token-efficient consumption by Claude; --format=json|table|csv for other consumers. This is the PostgreSQL counterpart to sql-query (which targets Azure SQL only).
metadata:
  category: tools
---

# psql-query (single-file C# script)

## Overview

One dotnet single-file script at `scripts/psql-query.cs`. Runs against any Azure Database for PostgreSQL the caller's credential can reach. Two independent read-only enforcement layers:

1. **Server-enforced read-only transaction** — every query runs inside `BEGIN; SET TRANSACTION READ ONLY;` and the transaction is **always rolled back**. Postgres itself rejects any write (`INSERT`/`UPDATE`/`DELETE`/DDL/`nextval`/`SELECT … INTO`) with *"cannot execute … in a read-only transaction"*. This is a hard server guarantee — unlike SQL Server (which has no read-only transaction mode and forces `sql-query` to rely on client-side AST parsing).
2. **Client-side guard (defense-in-depth)** — before the query is sent, every statement must lead with `SELECT`/`WITH`/`TABLE`/`VALUES`/`SHOW`/`EXPLAIN`. `SELECT … INTO`, data-modifying CTEs, and cross-source / file / admin functions (`dblink`, `pg_read_file`, `pg_ls_dir`, `lo_export`, `pg_terminate_backend`, …) are rejected with a clear reason. Comments, string literals (standard, `E'…'`, `$$…$$` dollar-quoted) and quoted identifiers are neutralized first, so `SELECT 'DELETE'` and `SELECT $$INSERT$$` still pass.

Auth via `DefaultAzureCredential` Entra token (scope `https://ossrdbms-aad.database.windows.net/.default`) used as the Postgres password, with `PGPASSWORD` / `--conn` fallback.

## Prerequisites

- **.NET 10+ SDK** (single-file script support; `dotnet run script.cs`).
- **Azure credential** on the target server:
  - Day-to-day: `az login` as a user whose AAD identity is a Postgres login role (or a member of an AAD group that is one).
  - CI/alerting: set `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` / `AZURE_TENANT_ID` for an SPN, and `--auth=env`.
- **The `--user` must be the AAD principal name** (or `PGUSER`) — Azure PG maps the token to the login role by name; the token alone does not identify the role the way SQL Server's `AccessToken` does.
- **DB grants** the login needs: `SELECT` on the tables you query; `pg_monitor` (or equivalent) for `pg_stat_*` views.
- **Password fallback**: set `PGPASSWORD` (e.g. sourced from your project's Key Vault) to use native password auth instead of Entra.

## Path variable

Set the script path once per session — works whether installed via plugin or as a user skill.

```powershell
# PowerShell
$psql = if ($env:CLAUDE_PLUGIN_ROOT) {
  "$env:CLAUDE_PLUGIN_ROOT\skills\psql-query\scripts"
} else {
  "$HOME\.claude\skills\psql-query\scripts"
}
```

```bash
# Bash
psql="${CLAUDE_PLUGIN_ROOT:-$HOME/.claude}/skills/psql-query/scripts"
```

## Host and database arguments

The first two positional args are `<host>` and `<database>`:

- `<host>` — the Azure Database for PostgreSQL Flexible Server FQDN, e.g. `your-server.postgres.database.azure.com`.
- `<database>` — the database name.

Like `sql-query`, this tool is deliberately **infrastructure-agnostic**: no server list is baked in. Record a project's actual hosts, databases, and key tables in that project's own resource skill (e.g. a `*-azure-topology` skill) or its `CLAUDE.md`, not here.

## Usage

### The one reliable shape (use this — same in PowerShell and bash)

**Always put `--` right after the script path**, then pass the query as `@file`:

```
dotnet run <script> -- <host> <database> "@<path-to-.sql>" --user=<principal> [flags]
```

Why `--`: `dotnet run` otherwise treats a leading-`@` argument as a .NET *response file* and expands it
(splitting the file's contents on whitespace into separate args) before the script sees it — you get
`unknown arg: ...` — and it can swallow reserved flags like `--help`. The `--` sends every following
token to the script untouched, so the script's own `@file` reader loads the query. The `.sql` file can
be **multi-line and readable**; the SQL never touches the shell, so there is no quoting, escaping, or
line-continuation for the shell to get wrong, and it behaves identically in both shells. Keep the whole
`dotnet run` on **one physical line** — no `` ` `` (PowerShell) or `\` (bash) continuations.

```powershell
# PowerShell — $base is your scratchpad/temp dir holding the .sql file
dotnet run "$psql\psql-query.cs" -- your-server.postgres.database.azure.com your-db "@$base\overlap.sql" --user=me@example.com --format=table --timeout=120
```

```bash
# Bash — $base is your scratchpad/temp dir holding the .sql file
dotnet run "$psql/psql-query.cs" -- your-server.postgres.database.azure.com your-db "@$base/overlap.sql" --user=me@example.com --format=table --timeout=120
```

### Trivial one-liners

A short query can be passed inline instead of via a file — still put `--` first:

```powershell
dotnet run "$psql\psql-query.cs" -- your-server.postgres.database.azure.com your-db "SELECT COUNT(*) FROM public.your_table" --user=me@example.com
```

### PostgreSQL-specific scenarios

```powershell
# Connect as an AAD *group* login: pass the group name as --user, your own token
dotnet run "$psql\psql-query.cs" -- your-server.postgres.database.azure.com admin "@$base\peek.sql" --user=<your-aad-group-login>

# Password auth (Key Vault) instead of Entra
$env:PGPASSWORD = "<from-keyvault>"
dotnet run "$psql\psql-query.cs" -- your-server.postgres.database.azure.com your-db "SELECT version()" --user=<db-role>

# Full connection string (bypasses host/user assembly)
dotnet run "$psql\psql-query.cs" -- ignored ignored "SELECT 1" --conn="Host=…;Database=…;Username=…;Password=…;SSL Mode=Require"

# Validate SQL without connecting (CI lint)
dotnet run "$psql\psql-query.cs" -- h db "SELECT 1" --dry-run

# Run the built-in read-only guard test battery
dotnet run "$psql\psql-query.cs" -- --selftest
```

To authenticate as an AAD **group** login, pass the group name to `--user` (e.g. `<your-aad-group-login>`) and your own token — the individual UPN is not a role. Group membership changes require a fresh token (re-`az login` if a just-added membership isn't recognized).

### Common flag combinations

Append to the command (after the query arg):

- `--format=json --max-rows=10000` — JSON envelope for piping into `jq` (alerting pipelines)
- `--timeout=120` — bump for slow analytical queries (max 300)
- `--dry-run` — validate the SQL against the read-only guard without connecting

## Output formats

- **`--format=toon`** (default) — token-efficient. Top-level `key: value` envelope + tabular `rows[N]{col,…}:` block. Best when the consumer is Claude.
- **`--format=json`** — pretty-printed JSON envelope `{ ok, error?, elapsed_ms, credential, row_count, truncated, columns, rows }`. Best for `jq` pipelines.
- **`--format=table`** — fixed-width ASCII for human eyes / Slack.
- **`--format=csv`** — RFC 4180 CSV, no envelope.

Postgres arrays render as `{a,b,c}`, `bytea` as `0x…`, `timestamptz`/`uuid` via invariant ISO formatting.

## Caps and timeouts

| Flag | Default | Notes |
|---|---|---|
| `--max-rows=N` | 500 | Hard client cap; sets `truncated: true` when hit. |
| `--max-cell-bytes=N` | 4000 | Per-string-cell budget; guards against `jsonb`/`text` blobs. |
| `--timeout=SECONDS` | 30 | Max 300. Applied as both Npgsql `CommandTimeout` and server `SET LOCAL statement_timeout`. |

## When the query is rejected

Exit code `2` with an envelope like:

```
ok: false
error: "validation: UPDATE not permitted (only SELECT/WITH/TABLE/VALUES/SHOW/EXPLAIN in read-only mode)"
```

**Fix the SQL — do not retry the same query.** The guard is read-only by design. Common rejections:
- `<KEYWORD> not permitted (only SELECT/WITH/…)` — statement leads with DML/DDL/DCL/`SET`/`COPY`/`CALL`/`DO`.
- `SELECT … INTO (creates a table) not permitted` — use `CREATE TABLE AS` elsewhere, not here.
- `data-modifying statement not permitted` — a data-modifying CTE or `EXPLAIN ANALYZE` of a write.
- `<fn>(...) not permitted (cross-source / file / admin function)` — `dblink`, `pg_read_file`, `lo_export`, etc.
- `unterminated dollar-quoted string` — malformed `$$…$$`.

If you genuinely need to mutate state, use a purpose-built script through change-management — not this tool.

## Exit codes

| Code | Meaning | Use in alerting |
|---|---|---|
| `0` | OK, rows returned (may be `truncated: true`) | `if rows.length > 0: page` |
| `2` | Client guard rejected the SQL | the SQL is wrong — fix it |
| `3` | Auth failed (no credential, token expired, missing `--user`) | re-`az login` / rotate SPN / pass `--user` |
| `4` | Postgres returned an error (syntax, permission, connection) | check `error` field |
| `5` | Query timed out (`statement_timeout` / command timeout) | bump `--timeout` or fix the query |
| `9` | Unexpected/unhandled | look at `error` field; file a bug |

## Self-test

`dotnet run scripts/psql-query.cs -- --selftest` runs a 45-case battery over the read-only guard (accepts SELECT/WITH/TABLE/VALUES/SHOW/EXPLAIN incl. keyword-in-string / dollar-quote / comment cases; rejects DML/DDL/DCL, `SELECT INTO`, multi-statement writes, and blocked functions). Exit `0` iff all pass. Run it after any change to the guard.

## Related

- `sql-query` — the Azure SQL / T-SQL counterpart (ScriptDom AST whitelist). Same envelope, formats, caps, and exit codes.
- `azure-servicebus-operations` — same single-file `.cs` + `DefaultAzureCredential` pattern for Service Bus.
