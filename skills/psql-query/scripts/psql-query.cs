#:package Npgsql@9.*
#:package Azure.Identity@1.13.*

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Npgsql;

#pragma warning disable IL2026 // single-file script, not trimmed
#pragma warning disable IL3050 // single-file script, not AOT-compiled

// -----------------------------------------------------------------------------
// psql-query.cs — read-only PostgreSQL runner with server-enforced RO txn + TOON
// -----------------------------------------------------------------------------
// Usage: dotnet run psql-query.cs <host> <database> <query-or-@file> [flags]
//
// Read-only by construction. Two independent layers:
//   1. Server-enforced read-only transaction — BEGIN; SET TRANSACTION READ ONLY;
//      Postgres itself rejects INSERT/UPDATE/DELETE/DDL/nextval/SELECT..INTO with
//      "cannot execute X in a read-only transaction". Always ROLLBACK at the end.
//   2. Client-side guard (defense-in-depth) — every statement must lead with
//      SELECT/WITH/TABLE/VALUES/SHOW/EXPLAIN; SELECT..INTO and cross-source /
//      file-access functions (dblink, pg_read_file, lo_export, ...) are rejected
//      before the query ever reaches the server.
//
// Auth: DefaultAzureCredential Entra token (scope ossrdbms-aad) used as the
// password for *.postgres.database.azure.com, with PGPASSWORD / --conn fallback.
//
// Exit codes:
//   0  ok                3  auth failed         5  server/statement timeout
//   2  validation reject 4  postgres error      9  unexpected
// -----------------------------------------------------------------------------

if (args.Length >= 1 && args[0] == "--selftest")
{
    return SelfTest.Run();
}

if (args.Length < 3 || args.Any(a => a is "--help" or "-h"))
{
    Console.Error.WriteLine("Usage: dotnet run psql-query.cs <host> <database> <query-or-@file> [flags]");
    Console.Error.WriteLine("  host         FQDN, e.g. pg-myserver.postgres.database.azure.com");
    Console.Error.WriteLine("  database     e.g. dell-orders");
    Console.Error.WriteLine("  query        SQL text, OR @path-to-file (recommended for non-trivial queries)");
    Console.Error.WriteLine("");
    Console.Error.WriteLine("Flags:");
    Console.Error.WriteLine("  --user=NAME                    AAD principal / Postgres role (or env PGUSER)");
    Console.Error.WriteLine("  --port=N                       default: 5432 (or env PGPORT)");
    Console.Error.WriteLine("  --conn=CONNSTRING              full Npgsql connection string (bypasses host/user assembly)");
    Console.Error.WriteLine("  --format=toon|json|table|csv   default: toon");
    Console.Error.WriteLine("  --max-rows=N                   default: 500   (truncates with truncated=true)");
    Console.Error.WriteLine("  --max-cell-bytes=N             default: 4000  (per-string-cell budget)");
    Console.Error.WriteLine("  --timeout=SECONDS              default: 30    (max 300)");
    Console.Error.WriteLine("  --auth=auto|aad|cli|mi|env     default: auto  (Entra token for azure.com hosts)");
    Console.Error.WriteLine("  --token=<bearer>               overrides credential discovery (used as password)");
    Console.Error.WriteLine("  --dry-run                      validate the SQL only; do not connect (exit 0/2)");
    Console.Error.WriteLine("  --verbose                      log which credential method succeeded");
    Console.Error.WriteLine("  --selftest                     run the built-in read-only guard test battery");
    return 1;
}

string host = args[0];
string database = args[1];
string queryArg = args[2];

string format = "toon";
int maxRows = 500;
int maxCellBytes = 4000;
int timeoutSec = 30;
string auth = "auto";
string? token = null;
string? user = Environment.GetEnvironmentVariable("PGUSER");
string? connStr = null;
int port = int.TryParse(Environment.GetEnvironmentVariable("PGPORT"), out var envPort) ? envPort : 5432;
bool verbose = false;
bool dryRun = false;

foreach (var a in args.Skip(3))
{
    if (a.StartsWith("--format=", StringComparison.Ordinal)) format = a["--format=".Length..].ToLowerInvariant();
    else if (a.StartsWith("--max-rows=", StringComparison.Ordinal)) maxRows = int.Parse(a["--max-rows=".Length..], CultureInfo.InvariantCulture);
    else if (a.StartsWith("--max-cell-bytes=", StringComparison.Ordinal)) maxCellBytes = int.Parse(a["--max-cell-bytes=".Length..], CultureInfo.InvariantCulture);
    else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutSec = Math.Clamp(int.Parse(a["--timeout=".Length..], CultureInfo.InvariantCulture), 1, 300);
    else if (a.StartsWith("--auth=", StringComparison.Ordinal)) auth = a["--auth=".Length..].ToLowerInvariant();
    else if (a.StartsWith("--token=", StringComparison.Ordinal)) token = a["--token=".Length..];
    else if (a.StartsWith("--user=", StringComparison.Ordinal)) user = a["--user=".Length..];
    else if (a.StartsWith("--port=", StringComparison.Ordinal)) port = int.Parse(a["--port=".Length..], CultureInfo.InvariantCulture);
    else if (a.StartsWith("--conn=", StringComparison.Ordinal)) connStr = a["--conn=".Length..];
    else if (a == "--dry-run") dryRun = true;
    else if (a == "--verbose") verbose = true;
    else { Console.Error.WriteLine($"unknown arg: {a}"); return 1; }
}

// Resolve @file query
string sql;
if (queryArg.StartsWith('@'))
{
    var path = queryArg[1..];
    if (!File.Exists(path)) { Console.Error.WriteLine($"query file not found: {path}"); return 1; }
    sql = File.ReadAllText(path);
}
else
{
    sql = queryArg;
}

// ----- 1. Client-side guard (defense-in-depth) --------------------------------
var (guardOk, guardReason) = Guard.ValidateReadOnly(sql);
if (!guardOk)
{
    EmitError(format, $"validation: {guardReason}", credential: null);
    return 2;
}

if (dryRun)
{
    EmitOk(format, new() { "validation" }, new() { new object?[] { "ok" } }, truncated: false, elapsedMs: 0, credential: "dry-run");
    return 0;
}

// ----- 2. Connect + run inside a server-enforced read-only transaction --------
var sw = System.Diagnostics.Stopwatch.StartNew();
string credentialUsed = "unknown";
try
{
    var effectiveConn = BuildConnectionString(connStr, host, port, database, user, auth, token, verbose, out credentialUsed);

    using var conn = new NpgsqlConnection(effectiveConn);
    conn.Open();

    using var tx = conn.BeginTransaction();

    // Belt-and-suspenders: make the whole transaction read-only at the server, and
    // bound server-side execution time independently of the client command timeout.
    using (var pre = conn.CreateCommand())
    {
        pre.Transaction = tx;
        pre.CommandText = $"SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = {timeoutSec * 1000};";
        pre.ExecuteNonQuery();
    }

    using var cmd = conn.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandTimeout = timeoutSec;
    cmd.CommandText = sql;

    List<string> cols;
    List<object?[]> rows;
    bool truncated;
    using (var rdr = cmd.ExecuteReader())
    {
        (cols, rows, truncated) = ReadCapped(rdr, maxRows, maxCellBytes);
    }

    tx.Rollback();
    sw.Stop();

    EmitOk(format, cols, rows, truncated, sw.ElapsedMilliseconds, credentialUsed);
    return 0;
}
catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.QueryCanceled)
{
    sw.Stop();
    EmitError(format, $"timeout after {sw.ElapsedMilliseconds}ms (limit {timeoutSec}s)", credentialUsed);
    return 5;
}
catch (PostgresException ex)
{
    sw.Stop();
    EmitError(format, $"postgres: {ex.MessageText} (SqlState={ex.SqlState}{(ex.Position > 0 ? $", Position={ex.Position}" : string.Empty)})", credentialUsed);
    return 4;
}
catch (NpgsqlException ex) when (ex.InnerException is TimeoutException || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
{
    sw.Stop();
    EmitError(format, $"timeout after {sw.ElapsedMilliseconds}ms (limit {timeoutSec}s)", credentialUsed);
    return 5;
}
catch (CredentialUnavailableException ex)
{
    sw.Stop();
    EmitError(format, $"auth-unavailable: {ex.Message}", credentialUsed);
    return 3;
}
catch (AuthenticationFailedException ex)
{
    sw.Stop();
    EmitError(format, $"auth: {ex.Message}", credentialUsed);
    return 3;
}
catch (NpgsqlException ex)
{
    sw.Stop();
    EmitError(format, $"connection: {ex.Message}", credentialUsed);
    return 4;
}
catch (ArgumentException ex)
{
    sw.Stop();
    EmitError(format, ex.Message, credentialUsed);
    return 3;
}
catch (Exception ex)
{
    sw.Stop();
    EmitError(format, $"unexpected: {ex.GetType().Name}: {ex.Message}", credentialUsed);
    return 9;
}

// =============================================================================
// CONNECTION + AUTH
// =============================================================================

static string BuildConnectionString(string? connStr, string host, int port, string database, string? user, string auth, string? token, bool verbose, out string credentialUsed)
{
    NpgsqlConnectionStringBuilder csb;
    if (!string.IsNullOrEmpty(connStr))
    {
        csb = new NpgsqlConnectionStringBuilder(connStr) { ApplicationName = "psql-query.cs" };
        credentialUsed = "conn-string";
        if (verbose) Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:o}] auth: conn-string → {csb.Host}/{csb.Database}");
        return csb.ConnectionString;
    }

    csb = new NpgsqlConnectionStringBuilder
    {
        Host = host,
        Port = port,
        Database = database,
        Username = user,
        SslMode = SslMode.Require,
        Timeout = 15,
        ApplicationName = "psql-query.cs",
    };

    bool azure = host.EndsWith(".postgres.database.azure.com", StringComparison.OrdinalIgnoreCase);
    var pgPassword = Environment.GetEnvironmentVariable("PGPASSWORD");
    bool forceAad = auth is "aad" or "cli" or "mi" or "env";

    if (!string.IsNullOrEmpty(token))
    {
        csb.Password = token;
        credentialUsed = "explicit-token";
    }
    else if (!string.IsNullOrEmpty(pgPassword) && !forceAad)
    {
        csb.Password = pgPassword;
        credentialUsed = "pgpassword-env";
    }
    else if (azure || forceAad)
    {
        if (string.IsNullOrEmpty(user))
        {
            throw new ArgumentException("Entra auth requires --user=<aad-principal> (or PGUSER). The Postgres login role must match the AAD identity.");
        }

        TokenCredential cred = auth switch
        {
            "cli" => new AzureCliCredential(),
            "mi" => new ManagedIdentityCredential(),
            "env" => new EnvironmentCredential(),
            _ => new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = true,
                ExcludeVisualStudioCodeCredential = false,
            }),
        };
        var tr = cred.GetToken(new TokenRequestContext(new[] { "https://ossrdbms-aad.database.windows.net/.default" }), default);
        csb.Password = tr.Token;
        credentialUsed = cred.GetType().Name;
    }
    else
    {
        throw new ArgumentException("No credential: set PGPASSWORD, pass --token/--conn, or target an *.postgres.database.azure.com host for Entra auth.");
    }

    if (verbose) Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:o}] auth: {credentialUsed} → {host}:{port}/{database}");
    return csb.ConnectionString;
}

// =============================================================================
// RESULT READER (capped by rows AND per-cell bytes)
// =============================================================================

static (List<string> cols, List<object?[]> rows, bool truncated) ReadCapped(NpgsqlDataReader rdr, int maxRows, int maxCellBytes)
{
    var cols = new List<string>(rdr.FieldCount);
    for (int i = 0; i < rdr.FieldCount; i++) cols.Add(rdr.GetName(i));

    var rows = new List<object?[]>(Math.Min(maxRows, 64));
    bool truncated = false;

    while (rdr.Read())
    {
        if (rows.Count >= maxRows) { truncated = true; break; }
        var row = new object?[rdr.FieldCount];
        for (int i = 0; i < rdr.FieldCount; i++)
        {
            var v = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
            if (v is string s && Encoding.UTF8.GetByteCount(s) > maxCellBytes)
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                var trimmed = Encoding.UTF8.GetString(bytes, 0, maxCellBytes);
                v = trimmed + "…[truncated]";
                truncated = true;
            }

            row[i] = v;
        }

        rows.Add(row);
    }

    return (cols, rows, truncated);
}

// =============================================================================
// OUTPUT WRITERS
// =============================================================================

static void EmitOk(string format, List<string> cols, List<object?[]> rows, bool truncated, long elapsedMs, string credential)
{
    switch (format)
    {
        case "json": WriteJson(cols, rows, truncated, elapsedMs, credential, ok: true, error: null); break;
        case "table": WriteTable(cols, rows, truncated, elapsedMs, credential); break;
        case "csv": WriteCsv(cols, rows); break;
        default: WriteToon(cols, rows, truncated, elapsedMs, credential, ok: true, error: null); break;
    }
}

static void EmitError(string format, string error, string? credential)
{
    switch (format)
    {
        case "json": WriteJson(new(), new(), truncated: false, elapsedMs: 0, credential ?? string.Empty, ok: false, error: error); break;
        case "table": Console.Error.WriteLine($"ERROR: {error}"); break;
        case "csv": Console.Error.WriteLine($"ERROR,{CsvField(error)}"); break;
        default: WriteToon(new(), new(), truncated: false, elapsedMs: 0, credential ?? string.Empty, ok: false, error: error); break;
    }
}

static void WriteToon(List<string> cols, List<object?[]> rows, bool truncated, long elapsedMs, string credential, bool ok, string? error)
{
    var sb = new StringBuilder();
    sb.Append("ok: ").Append(ok ? "true" : "false").Append('\n');
    if (error is not null) sb.Append("error: ").Append(ToonScalar(error)).Append('\n');
    sb.Append("elapsed_ms: ").Append(elapsedMs).Append('\n');
    sb.Append("credential: ").Append(ToonScalar(credential)).Append('\n');
    sb.Append("row_count: ").Append(rows.Count).Append('\n');
    sb.Append("truncated: ").Append(truncated ? "true" : "false").Append('\n');

    if (cols.Count > 0)
    {
        sb.Append("rows[").Append(rows.Count).Append("]{");
        for (int i = 0; i < cols.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(ToonHeader(cols[i]));
        }

        sb.Append("}:\n");
        foreach (var r in rows)
        {
            sb.Append("  ");
            for (int i = 0; i < r.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ToonCell(r[i]));
            }

            sb.Append('\n');
        }
    }

    Console.Write(sb.ToString());
}

static string ToonHeader(string name) =>
    name.Any(c => c is ',' or '{' or '}' or '\n' or '"') ? '"' + name.Replace("\"", "\"\"") + '"' : name;

static string ToonScalar(string s)
{
    if (s.Length == 0) return "\"\"";
    if (s.Any(c => c is ',' or ':' or '\n' or '"' or '#')) return '"' + s.Replace("\"", "\"\"") + '"';
    return s;
}

static string ToonCell(object? v)
{
    if (v is null) return "null";
    if (v is bool b) return b ? "true" : "false";
    if (v is DateTime dt) return dt.ToString("o", CultureInfo.InvariantCulture);
    if (v is DateTimeOffset dto) return dto.ToString("o", CultureInfo.InvariantCulture);
    if (v is byte[] bytes) return $"0x{Convert.ToHexString(bytes)}";
    if (v is Array arr) return QuoteIfNeeded(PgArray(arr));
    if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
    return QuoteIfNeeded(v.ToString() ?? string.Empty);
}

static string QuoteIfNeeded(string s) =>
    s.Any(c => c is ',' or '\n' or '"') ? '"' + s.Replace("\"", "\"\"") + '"' : s;

static string PgArray(Array arr)
{
    var parts = new List<string>(arr.Length);
    foreach (var e in arr)
    {
        parts.Add(e switch
        {
            null => "NULL",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => e.ToString() ?? string.Empty,
        });
    }

    return "{" + string.Join(",", parts) + "}";
}

static void WriteJson(List<string> cols, List<object?[]> rows, bool truncated, long elapsedMs, string credential, bool ok, string? error)
{
    using var doc = new MemoryStream();
    using (var w = new Utf8JsonWriter(doc, new JsonWriterOptions { Indented = true }))
    {
        w.WriteStartObject();
        w.WriteBoolean("ok", ok);
        if (error is not null) w.WriteString("error", error);
        w.WriteNumber("elapsed_ms", elapsedMs);
        w.WriteString("credential", credential);
        w.WriteNumber("row_count", rows.Count);
        w.WriteBoolean("truncated", truncated);

        w.WriteStartArray("columns");
        foreach (var c in cols) w.WriteStringValue(c);
        w.WriteEndArray();

        w.WriteStartArray("rows");
        foreach (var r in rows)
        {
            w.WriteStartArray();
            foreach (var cell in r) WriteJsonCell(w, cell);
            w.WriteEndArray();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    Console.Out.Write(Encoding.UTF8.GetString(doc.ToArray()));
    Console.Out.WriteLine();
}

static void WriteJsonCell(Utf8JsonWriter w, object? v)
{
    switch (v)
    {
        case null: w.WriteNullValue(); break;
        case bool b: w.WriteBooleanValue(b); break;
        case int i: w.WriteNumberValue(i); break;
        case long l: w.WriteNumberValue(l); break;
        case short s: w.WriteNumberValue(s); break;
        case byte by: w.WriteNumberValue(by); break;
        case double d: w.WriteNumberValue(d); break;
        case float f: w.WriteNumberValue(f); break;
        case decimal dec: w.WriteNumberValue(dec); break;
        case DateTime dt: w.WriteStringValue(dt.ToString("o", CultureInfo.InvariantCulture)); break;
        case DateTimeOffset dto: w.WriteStringValue(dto.ToString("o", CultureInfo.InvariantCulture)); break;
        case Guid g: w.WriteStringValue(g); break;
        case byte[] bytes: w.WriteStringValue("0x" + Convert.ToHexString(bytes)); break;
        case Array arr: w.WriteStringValue(PgArray(arr)); break;
        default: w.WriteStringValue(v.ToString() ?? string.Empty); break;
    }
}

static void WriteTable(List<string> cols, List<object?[]> rows, bool truncated, long elapsedMs, string credential)
{
    if (cols.Count == 0) { Console.WriteLine("(no columns)"); return; }
    var widths = new int[cols.Count];
    for (int i = 0; i < cols.Count; i++) widths[i] = cols[i].Length;
    var rendered = new List<string[]>(rows.Count);
    foreach (var r in rows)
    {
        var line = new string[r.Length];
        for (int i = 0; i < r.Length; i++)
        {
            line[i] = r[i] switch
            {
                null => "(null)",
                byte[] bytes => "0x" + Convert.ToHexString(bytes),
                Array arr => PgArray(arr),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => r[i]!.ToString() ?? string.Empty,
            };
            if (line[i].Length > widths[i]) widths[i] = Math.Min(line[i].Length, 60);
        }

        rendered.Add(line);
    }

    string Row(string[] cells) => string.Join(" | ", cells.Select((c, i) => c.Length > widths[i] ? c[..widths[i]] : c.PadRight(widths[i])));
    Console.WriteLine(Row(cols.ToArray()));
    Console.WriteLine(string.Join("-+-", widths.Select(w => new string('-', w))));
    foreach (var line in rendered) Console.WriteLine(Row(line));
    Console.WriteLine();
    Console.WriteLine($"({rows.Count} rows, {elapsedMs} ms, credential={credential}{(truncated ? ", TRUNCATED" : string.Empty)})");
}

static void WriteCsv(List<string> cols, List<object?[]> rows)
{
    Console.WriteLine(string.Join(",", cols.Select(CsvField)));
    foreach (var r in rows)
    {
        Console.WriteLine(string.Join(",", r.Select(v => CsvField(v switch
        {
            null => string.Empty,
            byte[] bytes => "0x" + Convert.ToHexString(bytes),
            Array arr => PgArray(arr),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? string.Empty,
        }))));
    }
}

static string CsvField(string s) =>
    s.Any(c => c is ',' or '"' or '\n' or '\r') ? '"' + s.Replace("\"", "\"\"") + '"' : s;

// =============================================================================
// READ-ONLY GUARD (defense-in-depth) — TDD'd via --selftest
// =============================================================================

static class Guard
{
    // Statements are permitted to lead only with these keywords. Everything else
    // (INSERT/UPDATE/DELETE/CREATE/DROP/ALTER/GRANT/SET/COPY/CALL/DO/...) is rejected
    // here; the server-side read-only transaction is the hard backstop.
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "WITH", "TABLE", "VALUES", "SHOW", "EXPLAIN",
    };

    // Cross-source readers, file access, and admin side-effects that can act
    // outside the read-only transaction's protection. Matched as whole words.
    private static readonly string[] BlockedFunctions =
    {
        "dblink", "dblink_exec",
        "pg_read_file", "pg_read_binary_file", "pg_stat_file",
        "pg_ls_dir", "pg_ls_logdir", "pg_ls_waldir",
        "lo_import", "lo_export", "lo_put", "lo_from_bytea",
        "pg_terminate_backend", "pg_cancel_backend",
        "pg_reload_conf", "pg_rotate_logfile",
    };

    public static (bool ok, string? reason) ValidateReadOnly(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return (false, "empty query");
        }

        var stripped = Strip(sql, out var stripError);
        if (stripError is not null)
        {
            return (false, stripError);
        }

        foreach (var fn in BlockedFunctions)
        {
            if (Regex.IsMatch(stripped, $@"\b{fn}\b", RegexOptions.IgnoreCase))
            {
                return (false, $"{fn}(...) not permitted (cross-source / file / admin function)");
            }
        }

        var statements = stripped.Split(';')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (statements.Count == 0)
        {
            return (false, "empty query");
        }

        foreach (var statement in statements)
        {
            var lead = FirstWord(statement);
            if (!Allowed.Contains(lead))
            {
                var shown = lead.Length == 0 ? "statement" : lead.ToUpperInvariant();
                return (false, $"{shown} not permitted (only SELECT/WITH/TABLE/VALUES/SHOW/EXPLAIN in read-only mode)");
            }

            // A SELECT/WITH/EXPLAIN can still carry a write: SELECT..INTO creates a table,
            // and data-modifying CTEs (WITH ... INSERT/UPDATE/DELETE/MERGE) or EXPLAIN ANALYZE
            // of a write execute one. FOR UPDATE row locks are intentionally NOT matched.
            if (lead is "SELECT" or "WITH" or "EXPLAIN")
            {
                if (Regex.IsMatch(statement, @"\bINTO\b", RegexOptions.IgnoreCase))
                {
                    return (false, "SELECT ... INTO (creates a table) not permitted");
                }

                if (Regex.IsMatch(statement, @"\bINSERT\b", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(statement, @"\bDELETE\s+FROM\b", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(statement, @"\bUPDATE\s+\S+\s+SET\b", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(statement, @"\bMERGE\b", RegexOptions.IgnoreCase))
                {
                    return (false, "data-modifying statement not permitted");
                }
            }
        }

        return (true, null);
    }

    // Neutralizes comments, string literals (standard, escape, dollar-quoted) and
    // quoted identifiers so keyword scanning cannot be fooled by 'DELETE' text or
    // $$ INSERT $$ payloads. Replaces each with a boundary-preserving placeholder.
    private static string Strip(string sql, out string? error)
    {
        error = null;
        var sb = new StringBuilder(sql.Length);
        int i = 0;
        int n = sql.Length;
        while (i < n)
        {
            char c = sql[i];

            if (c == '-' && i + 1 < n && sql[i + 1] == '-')
            {
                while (i < n && sql[i] != '\n') i++;
                continue;
            }

            if (c == '/' && i + 1 < n && sql[i + 1] == '*')
            {
                int depth = 1;
                i += 2;
                while (i < n && depth > 0)
                {
                    if (i + 1 < n && sql[i] == '/' && sql[i + 1] == '*') { depth++; i += 2; }
                    else if (i + 1 < n && sql[i] == '*' && sql[i + 1] == '/') { depth--; i += 2; }
                    else i++;
                }

                sb.Append(' ');
                continue;
            }

            if (c == '$')
            {
                int j = i + 1;
                while (j < n && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_')) j++;
                if (j < n && sql[j] == '$')
                {
                    var tag = sql.Substring(i, j - i + 1);
                    int close = sql.IndexOf(tag, j + 1, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        error = "unterminated dollar-quoted string";
                        return sb.ToString();
                    }

                    i = close + tag.Length;
                    sb.Append(" '' ");
                    continue;
                }
            }

            if ((c == 'e' || c == 'E') && i + 1 < n && sql[i + 1] == '\'' && (i == 0 || !IsWordChar(sql[i - 1])))
            {
                i += 2;
                while (i < n)
                {
                    if (sql[i] == '\\' && i + 1 < n) { i += 2; continue; }
                    if (sql[i] == '\'') { i++; break; }
                    i++;
                }

                sb.Append(" '' ");
                continue;
            }

            if (c == '\'')
            {
                i++;
                while (i < n)
                {
                    if (sql[i] == '\'' && i + 1 < n && sql[i + 1] == '\'') { i += 2; continue; }
                    if (sql[i] == '\'') { i++; break; }
                    i++;
                }

                sb.Append(" '' ");
                continue;
            }

            if (c == '"')
            {
                i++;
                while (i < n)
                {
                    if (sql[i] == '"' && i + 1 < n && sql[i + 1] == '"') { i += 2; continue; }
                    if (sql[i] == '"') { i++; break; }
                    i++;
                }

                sb.Append(" \"id\" ");
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static string FirstWord(string s)
    {
        int i = 0;
        while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == '(')) i++;
        int start = i;
        while (i < s.Length && char.IsLetter(s[i])) i++;
        return s[start..i];
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}

// =============================================================================
// SELF TEST — read-only guard battery (ships as a CI-lint feature)
// =============================================================================

static class SelfTest
{
    public static int Run()
    {
        (string sql, bool shouldPass)[] cases =
        {
            // ---- should PASS ----
            ("SELECT 1", true),
            ("select * from public.inbound_orders where order_type = 'ReplenishmentOrder'", true),
            ("WITH c AS (SELECT 1 AS x) SELECT * FROM c", true),
            ("TABLE public.outbound_orders", true),
            ("VALUES (1),(2),(3)", true),
            ("SELECT 'DELETE FROM x'", true),
            ("SELECT 'it''s fine' AS note", true),
            ("SELECT $$INSERT INTO evil$$ AS dollar_quoted", true),
            ("-- a leading comment\nSELECT 1", true),
            ("/* INSERT UPDATE DROP */ SELECT 1", true),
            ("SELECT e'line1\\nline2'", true),
            ("SELECT 1; SELECT 2", true),
            ("  ( SELECT 1 )  ", true),
            ("SELECT o.order_number FROM outbound_orders o JOIN inbound_orders i ON i.order_number = o.order_number", true),
            ("EXPLAIN SELECT 1", true),
            ("SHOW statement_timeout", true),
            ("SELECT * FROM outbound_orders WHERE order_number = '123' FOR UPDATE", true),
            ("SELECT ppids FROM inbound_order_lines WHERE ppids @> ARRAY['x']", true),

            // ---- should REJECT ----
            ("INSERT INTO t VALUES (1)", false),
            ("UPDATE t SET x = 1", false),
            ("DELETE FROM t", false),
            ("DROP TABLE t", false),
            ("TRUNCATE t", false),
            ("CREATE TABLE x (i int)", false),
            ("ALTER TABLE t ADD COLUMN c int", false),
            ("MERGE INTO t USING s ON t.id = s.id WHEN MATCHED THEN DELETE", false),
            ("GRANT ALL ON t TO public", false),
            ("SET ROLE admin", false),
            ("CALL some_proc()", false),
            ("DO $$ BEGIN PERFORM 1; END $$", false),
            ("COPY t TO '/tmp/out.csv'", false),
            ("SELECT * INTO newt FROM t", false),
            ("SELECT col INTO other FROM t", false),
            ("SELECT 1; DROP TABLE t", false),
            ("SELECT dblink('conn', 'SELECT 1')", false),
            ("SELECT pg_read_file('/etc/passwd')", false),
            ("SELECT lo_export(1, '/tmp/x')", false),
            ("SELECT pg_ls_dir('/')", false),
            ("SELECT pg_terminate_backend(123)", false),
            ("WITH d AS (DELETE FROM t RETURNING *) SELECT * FROM d", false),
            ("WITH u AS (UPDATE t SET x = 1 RETURNING *) SELECT * FROM u", false),
            ("WITH x AS (INSERT INTO t VALUES (1) RETURNING *) SELECT * FROM x", false),
            ("EXPLAIN ANALYZE DELETE FROM t", false),
            ("", false),
            ("   ", false),
        };

        int failures = 0;
        foreach (var (sql, shouldPass) in cases)
        {
            var (ok, reason) = Guard.ValidateReadOnly(sql);
            if (ok != shouldPass)
            {
                failures++;
                var preview = sql.Length > 48 ? sql[..48] + "…" : sql;
                preview = preview.Replace("\n", "\\n");
                Console.Error.WriteLine($"FAIL  expected {(shouldPass ? "PASS" : "REJECT")}, got {(ok ? "PASS" : "REJECT")}  [{preview}]  {(ok ? string.Empty : reason)}");
            }
        }

        Console.WriteLine($"selftest: {cases.Length - failures}/{cases.Length} passed");
        return failures == 0 ? 0 : 1;
    }
}
