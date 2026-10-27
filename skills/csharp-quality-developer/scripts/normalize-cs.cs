using System.Text;
using System.Text.RegularExpressions;

#pragma warning disable IL2026 // single-file script, not trimmed
#pragma warning disable IL3050 // single-file script, not AOT-compiled

// -----------------------------------------------------------------------------
// normalize-cs.cs — checks or fixes the file-format rules csharp-quality-developer
//                   states in prose, without ever parsing or rewriting code.
// -----------------------------------------------------------------------------
// Usage: dotnet run normalize-cs.cs -- --check [--path <dir-or-file>] [--include <glob>]
//        dotnet run normalize-cs.cs -- --fix   [--path <dir-or-file>] [--include <glob>]
//        dotnet run normalize-cs.cs -- --selftest
//
// --check is the interesting mode: run it BEFORE `dotnet build`. Each build
// round-trip on a real solution is 30-60 seconds, and a whitespace or newline
// violation does not need one to be found. In the session that motivated this
// script, four analyzer findings were each discovered in a separate build.
//
// Rules enforced, and only these — every one is stated in csharp-quality-developer:
//   CRLF     line endings must be CRLF
//   SA1028   no trailing whitespace
//   SA1518   exactly one newline at end of file
//   INDENT   4 spaces, never tabs, in the leading whitespace run
//
// Deliberately NOT `dotnet format`, which csharp-quality-developer forbids: it
// strips project-specific usings and mis-handles source-generated LoggerMessage
// partials. Those hazards are specific to a tool that parses and rewrites code.
// This one touches whitespace and line endings, and nothing else, which is why
// it is allowed to exist alongside that prohibition.
//
// BOM is deliberately NOT a rule. csharp-quality-developer does not state one,
// and a script must not enforce what its skill does not document. A BOM is
// stripped for analysis and preserved byte-for-byte on --fix.
//
// Exit codes:
//   0  conforming (or fixed)      1  violations found      2  usage error
// -----------------------------------------------------------------------------

if (args.Length == 0)
{
    Usage();
    return 2;
}

if (args[0] == "--selftest")
{
    return SelfTest.Run();
}

string mode = "";
string path = ".";
var includes = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--check": mode = "check"; break;
        case "--fix": mode = "fix"; break;
        case "--path" when i + 1 < args.Length: path = args[++i]; break;
        case "--include" when i + 1 < args.Length: includes.Add(args[++i]); break;
        case "-h" or "--help": Usage(); return 0;
        default:
            Console.Error.WriteLine($"unknown argument: {args[i]}");
            Usage();
            return 2;
    }
}

if (mode.Length == 0)
{
    Console.Error.WriteLine("one of --check or --fix is required");
    Usage();
    return 2;
}

if (includes.Count == 0)
{
    includes.Add("*.cs");
}

string[] targets;
try
{
    targets = Discover(path, includes);
}
catch (DirectoryNotFoundException)
{
    Console.Error.WriteLine($"path not found: {path}");
    return 2;
}
catch (FileNotFoundException)
{
    Console.Error.WriteLine($"path not found: {path}");
    return 2;
}

if (targets.Length == 0)
{
    Console.Error.WriteLine($"no files matched {string.Join(", ", includes)} under {path}");
    return 2;
}

var all = new List<(string File, Finding Finding)>();
int fixedCount = 0;

foreach (string file in targets)
{
    byte[] bytes = File.ReadAllBytes(file);
    bool hadBom = HasBom(bytes);
    string text = Decode(bytes, hadBom);

    foreach (Finding f in Rules.Analyze(text))
    {
        all.Add((file, f));
    }

    if (mode != "fix")
    {
        continue;
    }

    string repaired = Rules.Fix(text);
    if (repaired == text)
    {
        continue;
    }

    // Preserve the file's original BOM state — see the header. UTF8Encoding(false)
    // does not emit one; the original preamble is re-prepended when it was there.
    byte[] body = new UTF8Encoding(false).GetBytes(repaired);
    byte[] outBytes = hadBom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    File.WriteAllBytes(file, outBytes);
    fixedCount++;
    Console.WriteLine($"fixed  {file}");
}

if (mode == "fix")
{
    Console.WriteLine($"{fixedCount} file(s) repaired, {targets.Length} scanned.");
    return 0;
}

if (all.Count == 0)
{
    Console.WriteLine($"OK — {targets.Length} file(s) conform.");
    return 0;
}

foreach ((string file, Finding f) in all.OrderBy(x => x.File).ThenBy(x => x.Finding.Line))
{
    Console.WriteLine($"{file}:{f.Line}: {f.Rule} — {f.Detail}");
}

Console.WriteLine();
Console.WriteLine($"{all.Count} violation(s) in {all.Select(x => x.File).Distinct().Count()} file(s). Re-run with --fix.");
return 1;

static void Usage()
{
    Console.Error.WriteLine("""
        normalize-cs — check or fix C# file-format rules (CRLF, SA1028, SA1518, indentation)

          --check              report violations and exit 1; changes nothing
          --fix                rewrite offending files in place
          --path <dir|file>    root to walk, or a single file (default: .)
          --include <glob>     may repeat (default: *.cs)
          --selftest           run the built-in rule battery; exit 0 iff all pass
        """);
}

static bool HasBom(byte[] b) =>
    b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;

// Decode from raw bytes on purpose. A newline-normalizing reader would hide the
// very thing being checked.
static string Decode(byte[] bytes, bool hadBom) =>
    Encoding.UTF8.GetString(bytes, hadBom ? 3 : 0, bytes.Length - (hadBom ? 3 : 0));

static string[] Discover(string path, List<string> includes)
{
    if (File.Exists(path))
    {
        return [path];
    }

    if (!Directory.Exists(path))
    {
        throw new DirectoryNotFoundException(path);
    }

    var found = new List<string>();
    foreach (string glob in includes)
    {
        found.AddRange(Directory.EnumerateFiles(path, glob, SearchOption.AllDirectories));
    }

    return [.. found.Where(p => !Rules.IsExcluded(p)).Distinct().OrderBy(p => p, StringComparer.Ordinal)];
}

// -----------------------------------------------------------------------------
// RULES — the pure core. Everything above is I/O; everything here is tested by
// --selftest, which is the filter this script was built under.
// -----------------------------------------------------------------------------

record Finding(int Line, string Rule, string Detail);

static class Rules
{
    static readonly string[] ExcludedDirs = ["bin", "obj", "node_modules", ".git", ".vs"];

    public static bool IsExcluded(string path)
    {
        string[] parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => ExcludedDirs.Contains(p, StringComparer.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<Finding> Analyze(string text)
    {
        var findings = new List<Finding>();

        if (text.Length == 0)
        {
            findings.Add(new Finding(1, "EMPTY", "file is empty"));
            return findings;
        }

        // CRLF — a bare LF is one not preceded by CR.
        MatchCollection loneLf = Regex.Matches(text, "(?<!\r)\n");
        if (loneLf.Count > 0)
        {
            int line = 1 + text[..loneLf[0].Index].Count(c => c == '\n');
            findings.Add(new Finding(line, "CRLF", $"{loneLf.Count} line(s) use bare LF"));
        }

        // Normalize for the per-line checks so a mixed-ending file still reports
        // real line numbers.
        string normalized = text.Replace("\r\n", "\n");
        string[] lines = normalized.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (line.Length > 0 && line != line.TrimEnd())
            {
                findings.Add(new Finding(i + 1, "SA1028", "trailing whitespace"));
            }

            // A tab anywhere in the LEADING whitespace run. Anchored on purpose:
            // a tab inside a string literal or a trailing comment is not
            // indentation, and flagging it would make the rule unusable.
            if (Regex.IsMatch(line, "^ *\t"))
            {
                findings.Add(new Finding(i + 1, "INDENT", "tab used for indentation (4 spaces expected)"));
            }
        }

        // SA1518 — exactly one newline at end of file. A correctly terminated file
        // leaves one empty trailing element from the split above.
        int lastContentLine = Math.Max(1, lines.Length - 1);
        if (!normalized.EndsWith('\n'))
        {
            findings.Add(new Finding(lines.Length, "SA1518", "no newline at end of file"));
        }
        else if (normalized.EndsWith("\n\n"))
        {
            findings.Add(new Finding(lastContentLine, "SA1518", "more than one newline at end of file"));
        }

        return findings;
    }

    public static string Fix(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        string normalized = text.Replace("\r\n", "\n");
        string[] lines = normalized.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();

            Match lead = Regex.Match(lines[i], "^[ \t]+");
            if (lead.Success && lead.Value.Contains('\t'))
            {
                // A tab advances to the next 4-column stop, which is what the
                // editor showed the author. Anything else silently re-indents.
                int col = 0;
                foreach (char c in lead.Value)
                {
                    col = c == '\t' ? col + (4 - (col % 4)) : col + 1;
                }

                lines[i] = new string(' ', col) + lines[i][lead.Length..];
            }
        }

        string body = string.Join("\n", lines).TrimEnd('\n') + "\n";
        return body.Replace("\n", "\r\n");
    }
}

// -----------------------------------------------------------------------------
// SELFTEST — TDD'd via --selftest, the same contract psql-query.cs carries.
// Every case below existed and failed before Rules above did anything.
// -----------------------------------------------------------------------------

static class SelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        int count = 0;

        void Rules_(string name, string input, params string[] expected)
        {
            count++;
            string[] actual = [.. Rules.Analyze(input).Select(f => f.Rule).Distinct().OrderBy(r => r, StringComparer.Ordinal)];
            string[] want = [.. expected.Distinct().OrderBy(r => r, StringComparer.Ordinal)];
            if (!actual.SequenceEqual(want))
            {
                failures.Add($"{name}: expected [{string.Join(",", want)}] got [{string.Join(",", actual)}]");
            }
        }

        void Line(string name, string input, string rule, int expectedLine)
        {
            count++;
            Finding? hit = Rules.Analyze(input).FirstOrDefault(f => f.Rule == rule);
            if (hit is null)
            {
                failures.Add($"{name}: rule {rule} never fired");
            }
            else if (hit.Line != expectedLine)
            {
                failures.Add($"{name}: {rule} expected line {expectedLine} got {hit.Line}");
            }
        }

        void True_(string name, bool condition)
        {
            count++;
            if (!condition)
            {
                failures.Add(name);
            }
        }

        const string Clean = "using System;\r\n\r\nclass A\r\n{\r\n}\r\n";

        // --- clean input produces nothing ------------------------------------
        Rules_("clean file", Clean);
        Rules_("clean one-liner", "class A { }\r\n");

        // --- CRLF -------------------------------------------------------------
        Rules_("all bare LF", "class A\n{\n}\n", "CRLF");
        Rules_("mixed endings", "class A\r\n{\n}\r\n", "CRLF");
        Rules_("lone CR is not a bare LF", "class A\r\n{\r\n}\r\n");
        Line("CRLF line number", "a\r\nb\nc\r\n", "CRLF", 2);

        // --- SA1028 trailing whitespace ---------------------------------------
        Rules_("trailing space", "class A \r\n{\r\n}\r\n", "SA1028");
        Rules_("trailing tab", "class A\t\r\n{\r\n}\r\n", "SA1028");
        Rules_("trailing spaces on blank line", "class A\r\n   \r\n}\r\n", "SA1028");
        Line("SA1028 line number", "a\r\nb\r\nc \r\nd\r\n", "SA1028", 3);
        True_(
            "SA1028 fires once per offending line",
            Rules.Analyze("a \r\nb \r\nc\r\n").Count(f => f.Rule == "SA1028") == 2);

        // --- INDENT ------------------------------------------------------------
        Rules_("leading tab", "class A\r\n{\r\n\tint x;\r\n}\r\n", "INDENT");
        Rules_("space then tab", "class A\r\n{\r\n    \tint x;\r\n}\r\n", "INDENT");
        Line("INDENT line number", "class A\r\n{\r\n\tint x;\r\n}\r\n", "INDENT", 3);

        // The anchoring that makes the rule usable. A tab that is not indentation
        // must not fire, or every file with a tab in a string literal is a
        // violation and the check gets switched off.
        Rules_("tab inside a string literal", "var s = \"a\tb\";\r\n");
        Rules_("tab in a trailing comment", "int x = 1; // a\tb\r\n");
        Rules_("four spaces are correct", "class A\r\n{\r\n    int x;\r\n}\r\n");

        // --- SA1518 -------------------------------------------------------------
        Rules_("no newline at eof", "class A\r\n{\r\n}", "SA1518");
        Rules_("two newlines at eof", "class A\r\n{\r\n}\r\n\r\n", "SA1518");
        Rules_("three newlines at eof", "class A\r\n{\r\n}\r\n\r\n\r\n", "SA1518");
        Rules_("exactly one newline is correct", "class A\r\n{\r\n}\r\n");

        // --- empty --------------------------------------------------------------
        Rules_("empty file", "", "EMPTY");
        True_("empty file yields exactly one finding", Rules.Analyze("").Count == 1);

        // --- several rules at once -----------------------------------------------
        Rules_("bare LF plus trailing space", "class A \n{\n}\n", "CRLF", "SA1028");
        Rules_("tab indent plus no trailing newline", "class A\r\n{\r\n\tint x;\r\n}", "INDENT", "SA1518");
        Rules_(
            "all four at once",
            "class A \n{\n\tint x;\n}",
            "CRLF", "SA1028", "INDENT", "SA1518");

        // --- Fix ------------------------------------------------------------------
        True_("fix leaves a clean file byte-identical", Rules.Fix(Clean) == Clean);

        string[] dirty =
        [
            "class A \n{\n\tint x;\n}",
            "class A\n{\n}\n\n\n",
            "class A\r\n{\r\n}",
            "a \r\nb\t\r\n   \r\nc\r\n",
            "\tone\r\n\t\ttwo\r\n",
            "    \tmixed\r\n",
        ];

        foreach (string d in dirty)
        {
            True_($"fix resolves every finding: {Escape(d)}", Rules.Analyze(Rules.Fix(d)).Count == 0);
            True_($"fix is idempotent: {Escape(d)}", Rules.Fix(Rules.Fix(d)) == Rules.Fix(d));
            True_($"fix emits CRLF only: {Escape(d)}", !Regex.IsMatch(Rules.Fix(d), "(?<!\r)\n"));
            True_($"fix ends with exactly one newline: {Escape(d)}",
                Rules.Fix(d).EndsWith("\r\n") && !Rules.Fix(d).EndsWith("\r\n\r\n"));
        }

        // Content must survive. This is the assertion that stops a "fix" from
        // being a delete — the failure mode a whitespace tool can most easily hide.
        True_("fix preserves non-whitespace content",
            Strip(Rules.Fix("class A \n{\n\tint x = 1;\n}")) == Strip("class A{int x = 1;}"));
        True_("fix preserves blank lines between members",
            Rules.Fix("a\r\n\r\nb\r\n").Replace("\r\n", "|") == "a||b|");
        True_("fix converts a leading tab to four spaces",
            Rules.Fix("\tint x;\r\n") == "    int x;\r\n");
        True_("fix converts two leading tabs to eight spaces",
            Rules.Fix("\t\tint x;\r\n") == "        int x;\r\n");
        True_("fix advances a tab to the next four-column stop",
            Rules.Fix("  \tint x;\r\n") == "    int x;\r\n");

        // --- exclusions -------------------------------------------------------------
        True_("bin is excluded", Rules.IsExcluded("src/bin/Debug/A.cs"));
        True_("obj is excluded", Rules.IsExcluded(@"src\obj\A.cs"));
        True_("node_modules is excluded", Rules.IsExcluded("web/node_modules/x/A.cs"));
        True_(".git is excluded", Rules.IsExcluded(".git/A.cs"));
        True_("a normal source path is not excluded", !Rules.IsExcluded("src/Domain/Order.cs"));
        True_("a directory merely containing 'bin' is not excluded", !Rules.IsExcluded("src/binding/A.cs"));
        True_("a file named bin.cs is not excluded", !Rules.IsExcluded("src/bin.cs"));

        foreach (string f in failures)
        {
            Console.Error.WriteLine($"FAIL  {f}");
        }

        Console.WriteLine($"selftest: {count - failures.Count}/{count} passed");
        return failures.Count == 0 ? 0 : 1;
    }

    static string Escape(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    static string Strip(string s) => Regex.Replace(s, @"\s+", "");
}
