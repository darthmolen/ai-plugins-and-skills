---
name: sql-query
description: Use when running ad-hoc read-only T-SQL against Azure SQL — exploring Query Store, sampling tables, inspecting DMVs, joining sys.* views, or producing JSON/CSV for an alerting pipeline. Read-only by construction (AST-validated, rolled-back transaction, no INSERT/UPDATE/DELETE/EXEC/OPENROWSET). Returns TOON by default for token-efficient consumption by Claude; --format=json|table|csv for other consumers.
metadata:
  category: tools
---
# SQL Query (single-file C# script)

## Overview

One dotnet single-file script at `scripts/sql-query.cs`. Runs against any Azure SQL DB the caller's credential can reach. Three independent read-only enforcement layers:

1. **AST whitelist** — ScriptDom parses the SQL; only `SelectStatement` (and its CTE/DECLARE/SET-option wrappers) pass. Everything else is rejected before the query reaches the server.
2. **Reference walk** — `OPENROWSET`, `OPENQUERY`, and `EXECUTE` are rejected even when they appear inside a `SELECT`.
3. **Read-committed transaction always rolled back** — belt-and-suspenders; even if the validator missed something, nothing commits.

Auth via `DefaultAzureCredential` (env-SPN → MI → AzureCLI → VS). `az login` covers the common case.

## Prerequisites

- **.NET 10+ SDK** (single-file script support; `dotnet run script.cs`)
- **Azure credential** on the target server:
  - For day-to-day exploration: `az login` as a user in an AAD group that has DB access.
  - For alerting/CI: set `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` / `AZURE_TENANT_ID` env vars for an SPN.
- **DB grants** the credential needs:
  - `db_datareader` for table SELECTs.
  - **`VIEW DATABASE STATE`** for `sys.dm_*` and Query Store DMVs (`sys.query_store_*`). Without this, Query Store queries return empty silently with no error.

## Path variable

Set the script path once per session — works whether installed via plugin or as a user skill.

```powershell
# PowerShell
$sql = if ($env:CLAUDE_PLUGIN_ROOT) {
  "$env:CLAUDE_PLUGIN_ROOT\skills\sql-query\scripts"
} else {
  "$HOME\.claude\skills\sql-query\scripts"
}
```

```bash
# Bash
sql="${CLAUDE_PLUGIN_ROOT:-$HOME/.claude}/skills/sql-query/scripts"
```

## Server and database arguments

The first two positional args are the target `<server>` and `<database>`:

- `<server>` — the Azure SQL logical server FQDN, e.g. `your-server.database.windows.net`.
- `<database>` — the database name on that server.

This skill is deliberately **infrastructure-agnostic**: it hard-codes no server list. Which servers and databases exist for a given project is *that project's* concern and changes on a different cadence than this tool — keep that registry in the project's own resource skill (e.g. a `*-azure-topology` skill) or its `CLAUDE.md`, not here.

## Usage

### The one reliable shape (use this — same in PowerShell and bash)

**Always put `--` right after the script path**, then pass the query as `@file`:

```
dotnet run <script> -- <server> <database> "@<path-to-.sql>" [flags]
```

Why `--`: `dotnet run` otherwise treats a leading-`@` argument as a .NET **response file** and expands
it (splitting the file's contents on whitespace into separate args) *before* the script's own argument
parser runs — you get a failure like `unknown arg: SELECT TOP 20`. The `--` sends every following token
to the script untouched, so the script's own `@file` reader loads the query. The `.sql` file can be
**multi-line and readable**; the SQL never touches the shell, so there is no quoting, escaping, or
line-continuation for the shell to get wrong, and it behaves identically in both shells. Keep the whole
`dotnet run` on **one physical line** — no `` ` `` (PowerShell) or `\` (bash) continuations.

```powershell
# PowerShell — $base is your scratchpad/temp dir holding the .sql file
dotnet run "$sql\sql-query.cs" -- your-server.database.windows.net your-db "@$base\query.sql" --format=table --max-cell-bytes=8000 --timeout=120
```

```bash
# Bash — $base is your scratchpad/temp dir holding the .sql file
dotnet run "$sql/sql-query.cs" -- your-server.database.windows.net your-db "@$base/query.sql" --format=table --max-cell-bytes=8000 --timeout=120
```

### Trivial one-liners

A short query can be passed inline instead of via a file — still put `--` first:

```powershell
dotnet run "$sql\sql-query.cs" -- your-server.database.windows.net your-db "SELECT TOP 10 name, object_id FROM sys.tables ORDER BY name"
```

### Common flag combinations

Append these to the command (after the query arg):

- `--format=json --max-rows=10000` — JSON envelope for piping into `jq` (alerting pipelines)
- `--verbose` — report which credential method actually succeeded (auth diagnosis)
- `--auth=cli` — force a specific credential method, skipping the `DefaultAzureCredential` chain

## Output formats

- **`--format=toon`** (default) — token-efficient. Top-level `key: value` envelope + tabular `rows[N]{col,col,...}:` block. Best when the consumer is Claude reading the result.
- **`--format=json`** — pretty-printed JSON envelope `{ ok, error?, elapsed_ms, credential, row_count, truncated, columns, rows }`. Best for alerting pipelines that pipe into `jq`.
- **`--format=table`** — fixed-width ASCII columns for human eyes. Use when copy-pasting into Slack/email.
- **`--format=csv`** — RFC 4180 CSV, no envelope. Use when feeding another tool that wants pure data.

### TOON dialect (this skill)

```
ok: true
elapsed_ms: 432
credential: AzureCliCredential
row_count: 3
truncated: false
rows[3]{query_id,max_dur_ms,execs}:
  12223568,51647.2,156
  12222872,55491.0,5
  12254093,9770.3,136
```

- Shape annotation `rows[N]{col1,col2,...}:` tells the reader the schema once; rows are CSV-style underneath.
- Strings with commas/quotes/newlines are CSV-style double-quoted with `""` escaping.
- Nulls render as bare `null`.
- A truncated string cell gets `…[truncated]` appended (the marker is outside the byte budget).
- `truncated: true` at the envelope level means **either** row cap **or** any cell cap was hit.

## Caps and timeouts

| Flag | Default | Notes |
|---|---|---|
| `--max-rows=N` | 500 | Hard cap. Sets `truncated: true` when hit. Raise for alerting pipelines that need the full result. |
| `--max-cell-bytes=N` | 4000 | Per-string-cell budget. Protects against `query_sql_text` blobs blowing up the response. |
| `--timeout=SECONDS` | 30 | Max 300. Query Store joins to `runtime_stats_interval` can be slow; bump for big windows. |

## When the query is rejected

The script will reject with `exit code 2` and an envelope like:

```
ok: false
error: "validation: InsertStatement not permitted in read-only mode"
```

If you see this, **fix the SQL — do not retry the same query**. The validator is correct: this tool is read-only by design. If you genuinely need to mutate state, use a different tool (e.g., a one-off script with a service-principal scoped to the table you need to touch, run through change-management).

Common rejections and what they mean:
- `InsertStatement / UpdateStatement / DeleteStatement / MergeStatement not permitted` — DML
- `CreateTableStatement / DropTableStatement / AlterTableStatement not permitted` — DDL
- `ExecuteStatement not permitted` — `EXEC` / `sp_executesql` (no carve-out, by design)
- `OPENROWSET not permitted` / `OPENQUERY not permitted` — cross-source readers
- `parse error: ...` — the SQL is malformed; ScriptDom couldn't parse it at all

## Exit codes

| Code | Meaning | Use in alerting |
|---|---|---|
| `0` | OK, rows returned (may be `truncated: true`) | `if rows.length > 0: page` |
| `2` | AST validation rejected the SQL | the SQL is wrong — fix it |
| `3` | Auth failed (no credential or token expired) | re-`az login` or rotate SPN secret |
| `4` | Server returned a SQL error (syntax, permission denied, etc.) | check `error` field |
| `5` | Query timed out (per `--timeout`) | bump `--timeout` or fix the query |
| `9` | Unexpected/unhandled | look at `error` field; file a bug |

## Composability for alerting

The JSON envelope is designed to plug into a `jq`-driven alert pipeline:

```powershell
# Example: alert if any query in Query Store ran > 30s in the last hour
$result = dotnet run "$sql\sql-query.cs" -- your-server.database.windows.net your-db "@C:\alerts\slow-queries-1h.sql" --format=json --max-rows=100

$result | jq -e '.rows | length > 0' > $null
if ($LASTEXITCODE -eq 0) {
    # send to Slack / pager
}
```

Because validation runs client-side and the transaction always rolls back, this is safe to run from a scheduled job without supervision.

## Related

- `azure-servicebus-operations` — same pattern (DefaultAzureCredential, single-file `.cs`, `scripts/` layout) for Service Bus.
