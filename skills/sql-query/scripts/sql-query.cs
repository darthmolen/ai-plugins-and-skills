#:package Microsoft.Data.SqlClient@5.2.2
#:package Microsoft.SqlServer.TransactSql.ScriptDom@161.*
#:package Azure.Identity@1.13.2

using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;

#pragma warning disable IL2026 // single-file script, not trimmed
#pragma warning disable IL3050 // single-file script, not AOT-compiled

// -----------------------------------------------------------------------------
// sql-query.cs — read-only T-SQL runner with AST validation + TOON output
// -----------------------------------------------------------------------------
// Usage: dotnet run sql-query.cs <server> <database> <query-or-@file> [flags]
//
// Read-only by construction. Three independent layers:
//   1. ScriptDom whitelist parse — only SELECT (+ CTE/DECLARE/SET-option) wrappers allowed.
//   2. Table-reference walk      — OPENROWSET / OPENQUERY rejected explicitly.
//   3. READ COMMITTED transaction always rolled back at the end.
//
// Auth via DefaultAzureCredential by default (env-SPN → MI → AzureCLI → VS).
// Override with --auth=env|cli|mi or pass --token=<bearer>.
//
// TOON dialect (default output):
//   - top-level scalars:        "key: value"
//   - tabular array:            "name[count]{col1,col2,...}:\n  v1,v2,v3"
//   - quoted strings:           CSV-style, escape " by doubling
//   - null:                     bare "null"
//   - truncated cell suffix:    "…[truncated]"  (added OUTSIDE the byte budget)
//   - truncated rows flag:      top-level "truncated: true"
//
// Exit codes:
//   0  ok                3  auth failed         5  server timeout
//   2  validation reject 4  SQL error           9  unexpected
// -----------------------------------------------------------------------------

if (args.Length < 3 || args.Any(a => a is "--help" or "-h"))
{
    Console.Error.WriteLine("Usage: dotnet run sql-query.cs <server> <database> <query-or-@file> [flags]");
    Console.Error.WriteLine("  server       FQDN, e.g. sql-delta-prod.database.windows.net");
    Console.Error.WriteLine("  database     e.g. alpha-db");
    Console.Error.WriteLine("  query        SQL text, OR @path-to-file (recommended for non-trivial queries)");
    Console.Error.WriteLine("");
    Console.Error.WriteLine("Flags:");
    Console.Error.WriteLine("  --format=toon|json|table|csv   default: toon");
    Console.Error.WriteLine("  --max-rows=N                   default: 500   (truncates with truncated=true)");
    Console.Error.WriteLine("  --max-cell-bytes=N             default: 4000  (per-string-cell budget)");
    Console.Error.WriteLine("  --timeout=SECONDS              default: 30    (max 300)");
    Console.Error.WriteLine("  --auth=auto|env|cli|mi         default: auto  (DefaultAzureCredential)");
    Console.Error.WriteLine("  --token=<bearer>               overrides credential discovery");
    Console.Error.WriteLine("  --verbose                      log which credential method succeeded");
    return 1;
}

string server = args[0];
string database = args[1];
string queryArg = args[2];

string format = "toon";
int maxRows = 500;
int maxCellBytes = 4000;
int timeoutSec = 30;
string auth = "auto";
string? token = null;
bool verbose = false;

foreach (var a in args.Skip(3))
{
    if (a.StartsWith("--format=", StringComparison.Ordinal)) format = a["--format=".Length..].ToLowerInvariant();
    else if (a.StartsWith("--max-rows=", StringComparison.Ordinal)) maxRows = int.Parse(a["--max-rows=".Length..], CultureInfo.InvariantCulture);
    else if (a.StartsWith("--max-cell-bytes=", StringComparison.Ordinal)) maxCellBytes = int.Parse(a["--max-cell-bytes=".Length..], CultureInfo.InvariantCulture);
    else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutSec = Math.Clamp(int.Parse(a["--timeout=".Length..], CultureInfo.InvariantCulture), 1, 300);
    else if (a.StartsWith("--auth=", StringComparison.Ordinal)) auth = a["--auth=".Length..].ToLowerInvariant();
    else if (a.StartsWith("--token=", StringComparison.Ordinal)) token = a["--token=".Length..];
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

// ----- 1. AST validation (whitelist) ------------------------------------------------
var (astOk, astReason) = ValidateReadOnly(sql);
if (!astOk)
{
    EmitError(format, $"validation: {astReason}", credential: null);
    return 2;
}

// ----- 2. Open connection + run inside read-only transaction ------------------------
var sw = Stopwatch.StartNew();
string credentialUsed = "unknown";
try
{
    using var conn = OpenConnection(server, database, auth, token, verbose, out credentialUsed);
    using var tx = conn.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);
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
catch (SqlException ex) when (ex.Number == -2 || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
{
    sw.Stop();
    EmitError(format, $"timeout after {sw.ElapsedMilliseconds}ms (limit {timeoutSec}s)", credentialUsed);
    return 5;
}
catch (SqlException ex)
{
    sw.Stop();
    EmitError(format, $"sql: {ex.Message} (Number={ex.Number}, State={ex.State}, Line={ex.LineNumber})", credentialUsed);
    return 4;
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
catch (Exception ex)
{
    sw.Stop();
    EmitError(format, $"unexpected: {ex.GetType().Name}: {ex.Message}", credentialUsed);
    return 9;
}

// =============================================================================
// AST VALIDATION
// =============================================================================

static (bool ok, string? reason) ValidateReadOnly(string sql)
{
    var parser = new TSql160Parser(initialQuotedIdentifiers: true);
    var tree = parser.Parse(new StringReader(sql), out var parseErrors);
    if (parseErrors.Count > 0)
        return (false, $"parse error: {parseErrors[0].Message} (line {parseErrors[0].Line})");

    // Token-stream check: catches cross-source readers (OPENROWSET / OPENQUERY / OPENXML)
    // regardless of which AST subclass ScriptDom chose — Visit(OpenRowsetTableReference)
    // doesn't fire for the BULK form (BulkOpenRowsetTableReference dispatches separately),
    // and ScriptDom tokenizes OPENROWSET BULK as a distinct keyword type. Match on text +
    // skip string-literal tokens so `SELECT 'OPENROWSET'` still passes.
    foreach (var token in tree.ScriptTokenStream)
    {
        if (token.TokenType == TSqlTokenType.AsciiStringLiteral) continue;
        if (token.TokenType == TSqlTokenType.UnicodeStringLiteral) continue;
        if (token.TokenType == TSqlTokenType.MultilineComment) continue;
        if (token.TokenType == TSqlTokenType.SingleLineComment) continue;
        if (string.IsNullOrEmpty(token.Text)) continue;
        if (string.Equals(token.Text, "OPENROWSET", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token.Text, "OPENQUERY",  StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token.Text, "OPENXML",    StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"{token.Text.ToUpperInvariant()} not permitted (cross-source reader)");
        }
    }

    var v = new ReadOnlyVisitor();
    tree.Accept(v);
    return v.Reason is null ? (true, null) : (false, v.Reason);
}

// =============================================================================
// CONNECTION + AUTH
// =============================================================================

static SqlConnection OpenConnection(string server, string database, string auth, string? token, bool verbose, out string credentialUsed)
{
    var csb = new SqlConnectionStringBuilder
    {
        DataSource = server,
        InitialCatalog = database,
        Encrypt = true,
        ConnectTimeout = 15,
        ApplicationName = "sql-query.cs",
    };
    var conn = new SqlConnection(csb.ConnectionString);

    if (!string.IsNullOrEmpty(token))
    {
        conn.AccessToken = token;
        credentialUsed = "explicit-token";
    }
    else
    {
        TokenCredential cred = auth switch
        {
            "env" => new EnvironmentCredential(),
            "cli" => new AzureCliCredential(),
            "mi" => new ManagedIdentityCredential(),
            _ => new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = true,
                ExcludeVisualStudioCodeCredential = false,
            }),
        };
        var tr = cred.GetToken(new TokenRequestContext(new[] { "https://database.windows.net/.default" }), default);
        conn.AccessToken = tr.Token;
        credentialUsed = cred.GetType().Name;
    }

    if (verbose) Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:o}] auth: {credentialUsed} → {server}/{database}");
    conn.Open();
    return conn;
}

// =============================================================================
// RESULT READER (capped by rows AND per-cell bytes)
// =============================================================================

static (List<string> cols, List<object?[]> rows, bool truncated) ReadCapped(SqlDataReader rdr, int maxRows, int maxCellBytes)
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
                // Truncate to byte budget, then suffix marker.
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
        case "json":  WriteJson(cols, rows, truncated, elapsedMs, credential, ok: true, error: null); break;
        case "table": WriteTable(cols, rows, truncated, elapsedMs, credential); break;
        case "csv":   WriteCsv(cols, rows); break;
        default:      WriteToon(cols, rows, truncated, elapsedMs, credential, ok: true, error: null); break;
    }
}

static void EmitError(string format, string error, string? credential)
{
    switch (format)
    {
        case "json":  WriteJson(new(), new(), truncated: false, elapsedMs: 0, credential ?? "", ok: false, error: error); break;
        case "table": Console.Error.WriteLine($"ERROR: {error}"); break;
        case "csv":   Console.Error.WriteLine($"ERROR,{CsvField(error)}"); break;
        default:      WriteToon(new(), new(), truncated: false, elapsedMs: 0, credential ?? "", ok: false, error: error); break;
    }
}

// --- TOON ---------------------------------------------------------------------

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
    if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
    var s = v.ToString() ?? "";
    if (s.Any(c => c is ',' or '\n' or '"')) return '"' + s.Replace("\"", "\"\"") + '"';
    return s;
}

// --- JSON ---------------------------------------------------------------------

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
        default: w.WriteStringValue(v.ToString() ?? ""); break;
    }
}

// --- Table (human eye) --------------------------------------------------------

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
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => r[i]!.ToString() ?? ""
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
    Console.WriteLine($"({rows.Count} rows, {elapsedMs} ms, credential={credential}{(truncated ? ", TRUNCATED" : "")})");
}

// --- CSV (RFC 4180) -----------------------------------------------------------

static void WriteCsv(List<string> cols, List<object?[]> rows)
{
    Console.WriteLine(string.Join(",", cols.Select(CsvField)));
    foreach (var r in rows)
    {
        Console.WriteLine(string.Join(",", r.Select(v => CsvField(v switch
        {
            null => "",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? ""
        }))));
    }
}

static string CsvField(string s) =>
    s.Any(c => c is ',' or '"' or '\n' or '\r') ? '"' + s.Replace("\"", "\"\"") + '"' : s;

// =============================================================================
// TYPE DECLARATIONS (must come after all top-level static helpers)
// =============================================================================

sealed class ReadOnlyVisitor : TSqlFragmentVisitor
{
    public string? Reason;

    // Whitelist of statement types allowed in read-only mode.
    static readonly HashSet<Type> Allowed = new()
    {
        typeof(SelectStatement),
        typeof(StatementWithCtesAndXmlNamespaces),   // WITH-CTE wrapper around SELECT
        typeof(DeclareVariableStatement),            // DECLARE @x INT = (SELECT ...)
        typeof(SetVariableStatement),                // SET @x = (SELECT ...)
        typeof(PredicateSetStatement),               // SET ANSI_NULLS, NOCOUNT, etc.
        typeof(SetTransactionIsolationLevelStatement),
        typeof(SetOnOffStatement),                   // SET XACT_ABORT etc.
    };

    public override void Visit(TSqlStatement node)
    {
        if (Reason is not null) return;
        if (!Allowed.Contains(node.GetType()))
            Reason = $"{node.GetType().Name} not permitted in read-only mode";
    }

    // Explicit reject for cross-source readers — these can pull from external sources
    // and live INSIDE a SelectStatement so the statement-whitelist alone doesn't catch them.
    public override void Visit(OpenRowsetTableReference node) =>
        Reason ??= "OPENROWSET not permitted";
    public override void Visit(OpenQueryTableReference node) =>
        Reason ??= "OPENQUERY not permitted";

    // Sanity: also catch EXEC inside a sub-expression (rare but possible via INSERT INTO ... EXEC,
    // which would be blocked at the statement level anyway, but defense in depth).
    public override void Visit(ExecuteStatement node) =>
        Reason ??= "EXECUTE not permitted";
    public override void Visit(ExecuteSpecification node) =>
        Reason ??= "EXEC specification not permitted";
}
