using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

#pragma warning disable IL2026 // single-file script, not trimmed
#pragma warning disable IL3050 // single-file script, not AOT-compiled

// -----------------------------------------------------------------------------
// mutate.cs — seeds one mutant, runs the suite, reports what died, ALWAYS reverts.
// -----------------------------------------------------------------------------
// Usage:
//   dotnet run mutate.cs -- --file <path> --find <text> --replace <text>
//                           --label <description> --test-cmd <command>
//                           [--expect-killed-by <substring>]...
//   dotnet run mutate.cs -- --selftest
//
// THE REVERT IS THE POINT. A hand-run mutation that crashes, times out, or is
// interrupted between "seed" and "revert" leaves a deliberate bug in the working
// tree, and the next commit ships it. In the motivating session three mutants
// were run by hand and survived that risk purely by luck. This restores the
// original bytes in a finally block AND on Ctrl-C / SIGTERM.
//
// THE EXIT CODE IS INVERTED. A mutant that nothing kills is a FAILURE, not a
// pass -- it demonstrates the suite stays green while the behavior is wrong.
//
//   0  MUTANT KILLED      the suite went red, and any --expect-killed-by fired
//   1  MUTANT SURVIVED    the suite passed with a deliberate fault in place
//   2  BUILD-KILLED       the mutant did not compile; this proves nothing
//   3  usage / anchor error
//   4  TIMED OUT          the suite never finished; nothing is known either way
//   5  COMMAND-FAILED     the test command never ran; no test was executed
//   6  UNATTRIBUTED       killed, but --expect-killed-by could not be checked
//   7  KILLED BY WRONG TEST
//
// 0 is the only success. 2, 4 and 5 are "inconclusive", and are kept distinct from
// 0 and 1 on purpose. Reporting any of them as a kill is false confidence -- it
// claims the tests caught a fault they never actually reached.
//
// COMMAND-FAILED exists because that claim was made, in the field, against 3.5.0: a
// --test-cmd whose path would not resolve under cmd.exe never started, the build
// succeeded, the shell returned 1, and this script printed MUTANT KILLED and exited
// 0. The 3.3.0 release notes had already claimed that defect fixed; what shipped was
// the psi.Arguments quoting below, which its own comment calls an unproven fix. The
// guard is in Mutation.NeverRan, and it is what actually closes it.
//
// BUILD-KILLED is reported distinctly because it is not a kill. Observed in the
// motivating session: removing a guard clause made a parameter unused, and
// -warnaserror turned IDE0060 into a build error, so the "mutant" never reached
// a test. A mutant must fail on an assertion, not on a compile.
//
// --test-cmd keeps this framework-agnostic. For MSTest.Sdk 3.8 note the bare --:
//   --test-cmd "dotnet test tests/Foo.Tests -- --filter FullyQualifiedName~Bar"
// Without it the filter is silently dropped and the whole suite runs.
// -----------------------------------------------------------------------------

if (args.Length >= 1 && args[0] == "--selftest")
{
    return SelfTest.Run();
}

string? file = null, find = null, replace = null, label = null, testCmd = null;
var expect = new List<string>();
int timeoutSeconds = 600;
bool showOutput = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--file" when i + 1 < args.Length: file = args[++i]; break;
        case "--find" when i + 1 < args.Length: find = args[++i]; break;
        case "--replace" when i + 1 < args.Length: replace = args[++i]; break;
        case "--label" when i + 1 < args.Length: label = args[++i]; break;
        case "--test-cmd" when i + 1 < args.Length: testCmd = args[++i]; break;
        case "--expect-killed-by" when i + 1 < args.Length: expect.Add(args[++i]); break;
        case "--timeout" when i + 1 < args.Length: timeoutSeconds = int.Parse(args[++i]); break;
        case "--show-output": showOutput = true; break;
        case "-h" or "--help": Usage(); return 0;
        default:
            Console.Error.WriteLine($"unknown argument: {args[i]}");
            Usage();
            return 3;
    }
}

// --replace may legitimately be empty (deleting a guard clause is a mutant), so
// it is checked for null rather than for emptiness.
if (file is null || find is null || replace is null || label is null || testCmd is null)
{
    Console.Error.WriteLine("--file, --find, --replace, --label and --test-cmd are all required.");
    Console.Error.WriteLine("(--replace \"\" is valid: it deletes the anchor.)");
    Usage();
    return 3;
}

if (!File.Exists(file))
{
    Console.Error.WriteLine($"source file not found: {file}");
    return 3;
}

byte[] original = File.ReadAllBytes(file);
string text = new UTF8Encoding(false).GetString(original);

int occurrences = Mutation.CountOccurrences(text, find);
if (occurrences != 1)
{
    Console.Error.WriteLine(
        $"anchor matched {occurrences} time(s) in {file}; expected exactly 1. Narrow --find.");
    Console.Error.WriteLine("An anchor matching twice means the mutation is not the one --label claims.");
    return 3;
}

// The revert must survive Ctrl-C and a kill, not only a thrown exception. These
// handlers plus the finally below are the whole reason this script exists.
bool reverted = false;
var revertLock = new object();

void Revert()
{
    lock (revertLock)
    {
        if (reverted)
        {
            return;
        }

        File.WriteAllBytes(file, original);
        reverted = true;
        Console.WriteLine($"{Environment.NewLine}MUTANT REVERTED ({file} restored byte-for-byte).");
    }
}

Console.CancelKeyPress += (_, e) => { e.Cancel = false; Revert(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => Revert();

int result;
Judgement? judgement = null;
try
{
    File.WriteAllBytes(file, new UTF8Encoding(false).GetBytes(Mutation.Apply(text, find, replace)));
    Console.WriteLine($"MUTANT SEEDED: {label}");
    Console.WriteLine($"  {file}");
    Console.WriteLine($"  - {Mutation.Truncate(find)}");
    Console.WriteLine($"  + {Mutation.Truncate(replace)}");
    Console.WriteLine();
    Console.WriteLine($"RUNNING: {testCmd}");

    (int exit, string output, bool timedOut) = Shell.Run(testCmd, timeoutSeconds);

    if (showOutput)
    {
        Console.WriteLine();
        Console.WriteLine($"--- captured output ({output.Length} bytes, exit {exit}) ---");
        Console.WriteLine(output.TrimEnd());
        Console.WriteLine("--- end captured output ---");
    }

    judgement = Mutation.Judge(exit, output, expect, timedOut);
    Console.WriteLine();

    switch (judgement.Kind)
    {
        case Verdict.TimedOut:
            Console.WriteLine($"TIMED OUT — the suite did not finish within {timeoutSeconds}s.");
            Console.WriteLine("This is NOT a kill and NOT a survival: nothing is known about the mutant.");
            Console.WriteLine("Raise --timeout, or narrow --test-cmd to the covering tests only.");
            break;

        case Verdict.BuildKilled:
            Console.WriteLine("BUILD-KILLED — the mutant did not compile; no test ever ran.");
            Console.WriteLine("This proves NOTHING about the tests. Choose a mutation that still builds.");
            foreach (string e in judgement.BuildErrors.Take(5))
            {
                Console.WriteLine($"  {e}");
            }

            break;

        case Verdict.CommandFailed:
            Console.WriteLine("COMMAND-FAILED — the test command never ran; no test was executed.");
            Console.WriteLine("This is NOT a kill and NOT a survival. Scoring it a kill would claim the");
            Console.WriteLine("tests caught a fault they never reached — the worst thing this tool can do.");
            foreach (string e in judgement.Evidence.Take(3))
            {
                Console.WriteLine($"  {e}");
            }

            Console.WriteLine("Check the --test-cmd path, its quoting, and any --filter that may match nothing.");
            break;

        case Verdict.Survived:
            Console.WriteLine($"MUTANT SURVIVED — the suite passed with '{label}' in place.");
            Console.WriteLine("The tests do not cover this behavior. Strengthen the assertion, then re-run.");
            Console.WriteLine("Per test-filter-development: a surviving mutant REJECTS the test, it does not note it.");
            break;

        case Verdict.Unattributed:
            Console.WriteLine("MUTANT KILLED — the suite went red, so the fault was caught.");
            Console.WriteLine("BUT no test name could be parsed from this runner's output, so");
            Console.WriteLine("--expect-killed-by could not be checked. Treating that as a failure:");
            Console.WriteLine("an unverifiable expectation is not a satisfied one.");
            Console.WriteLine("Drop --expect-killed-by, or teach Mutation.FailedTests this runner's format.");
            break;

        case Verdict.KilledByWrongTest:
            WriteKillLine(judgement);
            Console.WriteLine();
            Console.WriteLine("But these expected killers did NOT fire:");
            foreach (string m in judgement.MissingExpected)
            {
                Console.WriteLine($"  {m}");
            }

            Console.WriteLine("Something died, but not the test that was supposed to.");
            break;

        case Verdict.Killed:
            WriteKillLine(judgement);
            break;

        // NOT a catch-all. `default:` used to BE the killed arm, so adding a verdict
        // and forgetting its case printed "MUTANT KILLED" and exited 0 -- which is
        // precisely the defect this release fixes, reproduced inside its own fix.
        default:
            throw new InvalidOperationException($"unhandled verdict: {judgement.Kind}");
    }

    // Assigned ONCE, from the verdict. It used to be assigned in seven places here,
    // which is how the exit code and the record line came to disagree.
    result = Report.ExitCode(judgement.Kind);
}
finally
{
    Revert();
}

if (!reverted)
{
    Console.Error.WriteLine($"PANIC: {file} may still contain the mutant. Check git diff before committing.");
    return 1;
}

Console.WriteLine();
Console.WriteLine("Record this in the PR body or commit, per test-filter-development:");
Console.WriteLine($"  MUTATE: {label}");
Console.WriteLine($"          {testCmd}");
Console.WriteLine($"          {Record(judgement)}; restored, git diff clean.");

return result;

// Keyed off the VERDICT, never off the exit code. The null branch is unreachable
// today -- an exception before Judge propagates past the finally and never reaches
// here -- and exists so a future catch cannot make this line lie again.
static string Record(Judgement? j) =>
    j is null ? "error — the run did not complete" : Report.Describe(j.Kind);

static void WriteKillLine(Judgement j)
{
    // "by 0 test(s)" asserted a count that was not known. A runner this parser does
    // not recognize still gives a correct kill/survive verdict; what it cannot give
    // is the names, and saying so is the honest version.
    if (j.FailedTests.Count == 0)
    {
        Console.WriteLine("MUTANT KILLED — the suite went red, but this runner named no failing");
        Console.WriteLine("tests, so the killers are an unknown set.");
        return;
    }

    Console.WriteLine("MUTANT KILLED by:");
    foreach (string t in j.FailedTests)
    {
        Console.WriteLine($"  {t}");
    }
}

static void Usage()
{
    Console.Error.WriteLine("""
        mutate — seed one mutant, run the suite, report what died, always revert

          --file <path>              source file to mutate
          --find <text>              exact text to replace; must match EXACTLY once
          --replace <text>           substitute ("" deletes the anchor)
          --label <text>             human description, echoed into the record
          --test-cmd <command>       command to run; any framework
          --expect-killed-by <text>  test-name substring that MUST fail (repeatable)
          --timeout <seconds>        default 600
          --show-output              print exactly what the test command produced
          --selftest                 run the built-in battery; exit 0 iff all pass

        Exit: 0 killed | 1 survived | 2 build-killed | 3 usage | 4 timed out
              5 command-failed (never ran) | 6 unattributed | 7 killed by wrong test
        """);
}

static class Shell
{
    public static (int Exit, string Output, bool TimedOut) Run(string command, int timeoutSeconds)
{
    bool windows = OperatingSystem.IsWindows();
    var psi = new ProcessStartInfo
    {
        FileName = windows ? "cmd.exe" : "/bin/sh",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    if (windows)
    {
        // NOT ArgumentList: it applies Windows argv quoting rules, which cmd.exe
        // does not follow. `/s` plus one outer pair of quotes is the documented
        // cmd.exe contract -- strip the first and last quote, treat the rest
        // literally -- so it is the defensible way to hand cmd an arbitrary
        // command line.
        //
        // HONEST NOTE ON WHY THIS IS HERE. It was written to fix an observed
        // failure: a --test-cmd of the form
        //     dotnet run "C:/path/x.cs" -- --selftest
        // returned "Couldn't find a project to run", as though dotnet never
        // received the path, and the harness scored that non-zero exit as a KILL.
        // The failure stopped after this change -- but a later A/B of this line
        // against the ArgumentList version could NOT reproduce any difference, on
        // that command shape or on several others. So this is kept as the correct
        // contract, NOT as a proven fix, and the original failure is unexplained.
        //
        // What is genuinely load-bearing is the reporting, not this line: a command
        // that never ran must never be scored as a kill. If it recurs, run with
        // --show-output, which exists because of this episode and prints exactly
        // what the child produced.
        psi.Arguments = $"/s /c \"{command}\"";
    }
    else
    {
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
    }

    using var p = Process.Start(psi)!;
    var sb = new StringBuilder();
    p.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (sb) { sb.AppendLine(e.Data); } } };
    p.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (sb) { sb.AppendLine(e.Data); } } };
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();

    bool timedOut = !p.WaitForExit(timeoutSeconds * 1000);
    if (timedOut)
    {
        p.Kill(entireProcessTree: true);
    }

    // MANDATORY, and the reason this is not just WaitForExit(ms). The timeout
    // overload returns as soon as the process exits and does NOT wait for the
    // async output handlers to drain; the parameterless overload does. Without
    // this line the captured output is truncated or empty, and Judge then decides
    // on a partial transcript.
    //
    // Observed for real: a mutant WAS killed, its runner printed three "FAIL"
    // lines, and this function returned before any of them arrived -- so the
    // harness reported "no test name could be parsed" and could not check
    // --expect-killed-by. The pure battery cannot reach this; only running the
    // harness against a real suite did.
    p.WaitForExit();

    if (timedOut)
    {
        lock (sb)
        {
            sb.AppendLine($"[mutate] TIMEOUT after {timeoutSeconds}s");
        }
    }

    lock (sb)
    {
        return (p.ExitCode, sb.ToString(), timedOut);
    }
}
}

// -----------------------------------------------------------------------------
// MUTATION — the pure core. Tested by --selftest.
// -----------------------------------------------------------------------------

enum Verdict { Killed, Survived, BuildKilled, KilledByWrongTest, Unattributed, TimedOut, CommandFailed }

record Judgement(
    Verdict Kind,
    IReadOnlyList<string> FailedTests,
    IReadOnlyList<string> MissingExpected,
    IReadOnlyList<string> BuildErrors,
    IReadOnlyList<string> Evidence);

// -----------------------------------------------------------------------------
// REPORT — the verdict is the single source of truth for both the exit code and
// the record line. They used to be two representations of one fact: the exit code
// was assigned in seven places inside the console switch, and the record line was
// derived from that int rather than from the verdict. They disagreed, and the
// disagreement shipped -- a killed mutant recorded itself as SURVIVED.
//
// Both live here, pure, so --selftest can reach them. Neither was reachable before,
// in a script whose whole thesis is that untested reporting is how its defects got in.
// -----------------------------------------------------------------------------

static class Report
{
    public static int ExitCode(Verdict kind) => kind switch
    {
        Verdict.Killed => 0,
        Verdict.Survived => 1,
        Verdict.BuildKilled => 2,
        Verdict.TimedOut => 4,
        Verdict.CommandFailed => 5,
        Verdict.Unattributed => 6,
        Verdict.KilledByWrongTest => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Describe(Verdict kind) => kind switch
    {
        Verdict.Killed => "killed",
        Verdict.Survived => "SURVIVED",
        Verdict.BuildKilled => "build-killed (inconclusive)",
        Verdict.TimedOut => "TIMED OUT (inconclusive)",
        Verdict.CommandFailed => "COMMAND FAILED, the test command never ran (inconclusive)",
        Verdict.Unattributed => "killed, but UNATTRIBUTED -- --expect-killed-by could not be checked",
        Verdict.KilledByWrongTest => "killed by the WRONG test(s)",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

static class Mutation
{
    public static int CountOccurrences(string haystack, string needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        int count = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    // Replaces the single occurrence only. String.Replace would swap every match,
    // and the exactly-once gate exists so that can never silently differ.
    public static string Apply(string text, string find, string replace)
    {
        int at = text.IndexOf(find, StringComparison.Ordinal);
        return at < 0 ? text : string.Concat(text.AsSpan(0, at), replace, text.AsSpan(at + find.Length));
    }

    // The trailing colon is load-bearing, not decoration. MSBuild always emits
    // "error CS0103: message", so requiring it costs nothing real -- while WITHOUT it,
    // a test whose assertion message mentions a diagnostic code ("expected error SA1028
    // got none") is scored BUILD-KILLED and a genuine kill is reported as "no test ever
    // ran". That became live the moment MTP failure lines gained a diagnostic-shaped
    // prefix; the explicit "error test failed:" exclusion is the second belt.
    //
    // MSB, NETSDK and NU are here because without them a failed restore (error NU1101)
    // or a wrong project path (error MSB1009) fell through to a FALSE KILL -- the same
    // class as the never-run command below, a different trigger.
    public static IReadOnlyList<string> BuildErrors(string output) =>
        [.. output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.Contains("error test failed:", StringComparison.OrdinalIgnoreCase))
            .Where(l => Regex.IsMatch(l, @"(?<![\w])error\s+(CS|CA|IDE|SA|NETSDK|MSB|NU)[0-9]+:"))
            .Distinct()];

    public static IReadOnlyList<string> FailedTests(string output)
    {
        var names = new List<string>();

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();

            // MSTest under MTP via `dotnet test`:
            //   "C:\...\Tests.cs(80): error test failed: Method (85ms): Expected ...  [C:\...\csproj]"
            // The assertion body is emitted as FURTHER "error test failed:" lines that
            // carry no "(<duration>):" group, so naive matching yields fragments like
            // "{" and `DefectId = "D1",`. The trailing COLON is what excludes them --
            // not the duration, which is why the duration may also be seconds. MTP
            // renders anything over a second as "(1.2s)", and matching only "ms" would
            // silently drop the slow test that killed the mutant.
            //
            // (.+?) rather than (\S+): a parameterised display name contains spaces.
            Match mtpDotnetTest = Regex.Match(
                line, @"error test failed:\s*(.+?)\s+\(\d+(?:[.,]\d+)?\s*(?:ms|s)\)\s*:");
            if (mtpDotnetTest.Success)
            {
                names.Add(mtpDotnetTest.Groups[1].Value.Trim());
                continue;
            }

            // MTP / MSTest.Sdk:  "failed Namespace.Class.Method (12ms)"
            Match mtp = Regex.Match(line, @"^failed\s+(\S+)", RegexOptions.IgnoreCase);
            if (mtp.Success)
            {
                names.Add(mtp.Groups[1].Value);
                continue;
            }

            // VSTest:  "  Failed Namespace.Class.Method [12 ms]"
            Match vstest = Regex.Match(line, @"^Failed\s+(\S+)");
            if (vstest.Success)
            {
                names.Add(vstest.Groups[1].Value);
                continue;
            }

            // xUnit:  "    Foo.Tests.Bar [FAIL]"   -- the name PRECEDES the token, which
            // is why none of the anchored patterns above ever caught it.
            Match xunit = Regex.Match(line, @"^(\S+)\s+\[FAIL\]");
            if (xunit.Success)
            {
                names.Add(xunit.Groups[1].Value);
                continue;
            }

            // NUnit3 console:  "1) Failed : Foo.Tests.Bar"  -- the leading "1) " defeats
            // the VSTest ^Failed anchor.
            Match nunit = Regex.Match(line, @"^\d+\)\s*Failed\s*:\s*(\S+)");
            if (nunit.Success)
            {
                names.Add(nunit.Groups[1].Value);
                continue;
            }

            // Jest / Vitest:  "✕ renders the thing"  /  "× renders the thing"
            Match jest = Regex.Match(line, @"^[✕×]\s+(.+?)(?:\s+\(\d+\s*ms\))?$");
            if (jest.Success)
            {
                names.Add(jest.Groups[1].Value.Trim());
                continue;
            }

            // pytest:  "FAILED tests/test_x.py::test_thing"
            Match pytest = Regex.Match(line, @"^FAILED\s+(\S+)");
            if (pytest.Success)
            {
                names.Add(pytest.Groups[1].Value);
                continue;
            }

            // Generic "FAIL  <name>[: detail]" — the shape the --selftest batteries
            // in this repo emit, and a common plain-harness convention. Anchored,
            // so "this will FAIL if you change it" is not a test name. Checked after
            // pytest because "FAILED" must not fall in here.
            Match generic = Regex.Match(line, @"^FAIL\s+(.+)$");
            if (generic.Success)
            {
                string name = generic.Groups[1].Value;
                int colon = name.IndexOf(':');
                names.Add((colon > 0 ? name[..colon] : name).Trim());
            }
        }

        return [.. names.Distinct()];
    }

    // Did the test command actually run? Returns the evidence line, or empty.
    //
    // THE HARD PART IS THE FALSE POSITIVES, not the detection. Every marker below is a
    // string the code under test can itself emit -- and the path/process mutants in
    // seeding-mutants.md's own catalogue are the ones most likely to make it. An xUnit
    // test whose SUT throws Win32Exception("The system cannot find the path specified")
    // is a TEXTBOOK KILL, and scoring it inconclusive would hide it. Hence two tiers,
    // both gated by the caller on no test name having parsed:
    //
    //   Tier 1, structural: cmd.exe (9009) and POSIX shells (127) both reserve an exit
    //   code for "could not resolve the command". No text involved, so nothing to fake.
    //
    //   Tier 2, textual: anchored, and only in the LAST THREE non-empty lines. A shell
    //   that could not resolve a command produces that text as the run's TERMINAL
    //   output; a SUT that throws produces it mid-transcript with runner summary lines
    //   after it. That positional constraint is what makes a substring list survivable.
    //
    // DELIBERATELY ABSENT, and each has a negative case in --selftest so it is not
    // "fixed" back in later:
    //   - bare ": not found"            matches `key 'alpha': not found` and every
    //                                   <thing>: not found assertion message ever written
    //   - "No such file or directory"   pure ENOENT text; emitted by the SUT far more
    //                                   often than by a shell, and NOT how POSIX shells
    //                                   report an unresolvable command (that is ": not found")
    //   - "Couldn't find a project to run"  comes from the unreproduced episode in
    //                                   Shell.Run's comment. Canonizing an unexplained
    //                                   observation into a rule is exactly what that
    //                                   comment refuses to do, and it is legitimate
    //                                   output when a test shells out to `dotnet run`.
    public static IReadOnlyList<string> NeverRan(string output, int exitCode)
    {
        string[] lines = [.. output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)];

        // Zero tests executed is the likelier never-ran case in this harness's own
        // workflow than an unresolvable path: --filter FullyQualifiedName~Ordreing
        // matches nothing and exits non-zero, which is the silent version of the
        // dropped-filter mistake the header comment already warns about. NOT position
        // constrained -- runners print this in a summary with more output after it.
        foreach (string l in lines)
        {
            if (Regex.IsMatch(l, @"(?i)(no test is available|zero tests ran|found 0 test)"))
            {
                return [l];
            }
        }

        if (exitCode is 9009 or 127)
        {
            return [$"[mutate] exit {exitCode}: the shell could not resolve the command"];
        }

        foreach (string l in lines.TakeLast(3))
        {
            if (Regex.IsMatch(l, @"^'[^']+' is not recognized as an internal or external command")
                || Regex.IsMatch(l, @"^The system cannot find the path specified")
                || Regex.IsMatch(l, @"^\S*(sh|bash|zsh|dash): (line \d+: |\d+: )?.*(command not found|: not found)$"))
            {
                return [l];
            }
        }

        return [];
    }

    public static Judgement Judge(
        int exitCode,
        string output,
        IReadOnlyList<string> expected,
        bool timedOut = false)
    {
        IReadOnlyList<string> buildErrors = BuildErrors(output);
        if (buildErrors.Count > 0)
        {
            return new Judgement(Verdict.BuildKilled, [], [], buildErrors, []);
        }

        // A timeout is NOT a kill. The suite never finished, so its exit code says
        // nothing about whether the mutant was caught. Reporting it as a kill is
        // false confidence: it claims the tests caught a fault they may never have
        // reached.
        if (timedOut)
        {
            return new Judgement(Verdict.TimedOut, [], [], [], []);
        }

        IReadOnlyList<string> failed = FailedTests(output);

        // BEFORE the exit-zero check, on purpose. A command that never ran CAN exit
        // zero -- `cmd /s /c "tests.exe & echo done"` does -- and a false SURVIVED is
        // the worse error to make here: per test-filter-development a surviving mutant
        // REJECTS the test, so it sends someone to strengthen an assertion that never
        // executed. Gated on no name having parsed, because names parsing proves the
        // tests ran and makes any marker in the output incidental.
        if (failed.Count == 0)
        {
            IReadOnlyList<string> evidence = NeverRan(output, exitCode);
            if (evidence.Count > 0)
            {
                return new Judgement(Verdict.CommandFailed, [], [], [], evidence);
            }
        }

        if (exitCode == 0)
        {
            return new Judgement(Verdict.Survived, [], [], [], []);
        }

        // The suite went red, so the mutant died — but if no test name could be
        // parsed out of this runner's output, --expect-killed-by cannot be checked.
        // Saying "the wrong test died" there would be a lie: nothing is known about
        // which test died. Report the ignorance instead of inventing a verdict.
        if (failed.Count == 0 && expected.Count > 0)
        {
            return new Judgement(Verdict.Unattributed, [], [], [], []);
        }

        IReadOnlyList<string> missing =
            [.. expected.Where(e => !failed.Any(f => f.Contains(e, StringComparison.OrdinalIgnoreCase)))];

        return missing.Count > 0
            ? new Judgement(Verdict.KilledByWrongTest, failed, missing, [], [])
            : new Judgement(Verdict.Killed, failed, [], [], []);
    }

    public static string Truncate(string s)
    {
        string flat = s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return flat.Length <= 100 ? flat : flat[..97] + "...";
    }
}

// -----------------------------------------------------------------------------
// SELFTEST
// -----------------------------------------------------------------------------

static class SelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        int count = 0;

        void True_(string name, bool condition)
        {
            count++;
            if (!condition)
            {
                failures.Add(name);
            }
        }

        void Eq(string name, object? actual, object? expected)
        {
            count++;
            if (!Equals(actual, expected))
            {
                failures.Add($"{name}: expected {expected} got {actual}");
            }
        }

        // --- the exactly-once anchor gate ---------------------------------------
        Eq("anchor absent", Mutation.CountOccurrences("abc", "z"), 0);
        Eq("anchor once", Mutation.CountOccurrences("a b c", "b"), 1);
        Eq("anchor twice", Mutation.CountOccurrences("b a b", "b"), 2);
        Eq("anchor thrice", Mutation.CountOccurrences("bbb", "b"), 3);
        Eq("overlapping is counted non-overlapping", Mutation.CountOccurrences("aaaa", "aa"), 2);
        Eq("empty needle never matches", Mutation.CountOccurrences("abc", ""), 0);
        Eq("multi-line anchor", Mutation.CountOccurrences("if (x)\n{\n}\n", "if (x)\n{"), 1);
        Eq("anchor match is case sensitive", Mutation.CountOccurrences("Foo", "foo"), 0);

        // --- Apply replaces exactly one occurrence -------------------------------
        Eq("apply replaces the anchor", Mutation.Apply("a b c", "b", "Z"), "a Z c");
        Eq("apply with empty replacement deletes", Mutation.Apply("a b c", " b", ""), "a c");
        Eq("apply replaces only the first", Mutation.Apply("b a b", "b", "Z"), "Z a b");
        Eq("apply on a missing anchor is a no-op", Mutation.Apply("abc", "z", "Z"), "abc");
        Eq("apply preserves CRLF around the edit",
            Mutation.Apply("a\r\nb\r\n", "b", "Z"), "a\r\nZ\r\n");
        True_("apply round-trips back to the original",
            Mutation.Apply(Mutation.Apply("x < y", "<", ">"), ">", "<") == "x < y");

        // --- BUILD-KILLED detection ----------------------------------------------
        True_("CS error detected", Mutation.BuildErrors("Foo.cs(3,5): error CS0103: nope").Count == 1);
        True_("IDE analyzer error detected — the observed real case",
            Mutation.BuildErrors("Foo.cs(3,5): error IDE0060: unused parameter").Count == 1);
        True_("SA error detected", Mutation.BuildErrors("error SA1028: trailing whitespace").Count == 1);
        True_("CA error detected", Mutation.BuildErrors("error CA1822: mark as static").Count == 1);
        True_("a warning is not a build error", Mutation.BuildErrors("warning CS0168: unused").Count == 0);
        True_("an assertion failure is not a build error",
            Mutation.BuildErrors("Assert.AreEqual failed. Expected:<90>. Actual:<100>.").Count == 0);
        True_("the word 'error' alone is not a build error",
            Mutation.BuildErrors("the test asserts on error handling").Count == 0);
        True_("a test named ...error... is not a build error",
            Mutation.BuildErrors("failed Foo.Tests.ThrowsOnErrorCode").Count == 0);

        // --- failed-test extraction, per framework --------------------------------
        True_("MTP failed line",
            Mutation.FailedTests("failed Foo.Tests.Bar (12ms)").SequenceEqual(["Foo.Tests.Bar"]));
        True_("VSTest failed line",
            Mutation.FailedTests("  Failed Foo.Tests.Bar [12 ms]").SequenceEqual(["Foo.Tests.Bar"]));
        True_("jest failed line",
            Mutation.FailedTests("✕ renders the thing (4 ms)").SequenceEqual(["renders the thing"]));
        True_("pytest failed line",
            Mutation.FailedTests("FAILED tests/test_x.py::test_thing").SequenceEqual(["tests/test_x.py::test_thing"]));
        True_("passing lines yield nothing",
            Mutation.FailedTests("passed Foo.Tests.Bar (1ms)\nok\n").Count == 0);
        True_("duplicates collapse",
            Mutation.FailedTests("failed A.B\nfailed A.B\n").Count == 1);
        True_("several failures are all captured",
            Mutation.FailedTests("failed A.B (1ms)\nfailed C.D (2ms)\n").Count == 2);

        // Found by running this harness against normalize-cs.cs for real: its
        // battery prints "FAIL  <name>", which none of the patterns above matched,
        // so a genuine kill was reported as "killed by 0 test(s)" and
        // --expect-killed-by produced a FALSE "wrong test died". The unit cases
        // above could not find this -- they only ever tested formats imagined here.
        True_("generic FAIL line",
            Mutation.FailedTests("FAIL  tab inside a string literal").SequenceEqual(["tab inside a string literal"]));
        True_("generic FAIL line with a colon detail",
            Mutation.FailedTests("FAIL  trailing tab: expected [SA1028] got [INDENT,SA1028]")
                .SequenceEqual(["trailing tab"]));
        True_("the word FAIL mid-sentence is not a test name",
            Mutation.FailedTests("this will FAIL if you change it").Count == 0);

        // --- an unattributable kill is honest about being unattributable -----------
        Eq("red suite with no parseable names is still a kill",
            Mutation.Judge(1, "something broke\n", []).Kind, Verdict.Killed);
        Eq("but it cannot confirm an expected killer",
            Mutation.Judge(1, "something broke\n", ["Wanted"]).Kind, Verdict.Unattributed);
        True_("an unattributed kill does not claim the wrong test died",
            Mutation.Judge(1, "something broke\n", ["Wanted"]).MissingExpected.Count == 0);

        // --- the verdict table, which is where the inverted exit code lives --------
        Eq("green suite means SURVIVED, not success",
            Mutation.Judge(0, "passed A.B\n", []).Kind, Verdict.Survived);
        Eq("red suite means KILLED",
            Mutation.Judge(1, "failed A.B (1ms)\n", []).Kind, Verdict.Killed);
        Eq("a build error outranks a non-zero exit",
            Mutation.Judge(1, "error CS0103: nope\n", []).Kind, Verdict.BuildKilled);
        Eq("a build error outranks a zero exit too",
            Mutation.Judge(0, "error CS0103: nope\n", []).Kind, Verdict.BuildKilled);
        Eq("expected killer fired",
            Mutation.Judge(1, "failed Foo.PreferWorkDirectory (1ms)\n", ["PreferWorkDirectory"]).Kind,
            Verdict.Killed);
        Eq("expected killer did not fire",
            Mutation.Judge(1, "failed Foo.SomethingElse (1ms)\n", ["PreferWorkDirectory"]).Kind,
            Verdict.KilledByWrongTest);
        Eq("expected killer matching is case-insensitive",
            Mutation.Judge(1, "failed Foo.PREFERWORKDIRECTORY (1ms)\n", ["preferworkdirectory"]).Kind,
            Verdict.Killed);
        True_("the missing expected killer is named",
            Mutation.Judge(1, "failed Foo.Other (1ms)\n", ["Wanted"]).MissingExpected.SequenceEqual(["Wanted"]));
        Eq("one expected killer missing out of two still fails",
            Mutation.Judge(1, "failed Foo.A (1ms)\n", ["A", "B"]).Kind,
            Verdict.KilledByWrongTest);
        True_("build-killed carries the errors for reporting",
            Mutation.Judge(1, "error CS0103: nope\n", []).BuildErrors.Count == 1);
        True_("survived reports no failed tests",
            Mutation.Judge(0, "passed A.B\n", []).FailedTests.Count == 0);

        // --- a timeout is not a kill -----------------------------------------------
        // Found by killing a real run mid-flight: the interrupted run reported
        // "MUTANT KILLED by 0 test(s)" and exited 0. Nothing ran to completion, so
        // that is false confidence of the same class as the unattributed bug above --
        // it tells you the suite caught a fault when the suite never finished.
        Eq("a timed-out run is not a kill",
            Mutation.Judge(1, "failed A.B (1ms)\n", [], timedOut: true).Kind, Verdict.TimedOut);
        Eq("a timed-out run with a green exit is also not a survival",
            Mutation.Judge(0, "", [], timedOut: true).Kind, Verdict.TimedOut);
        Eq("a timed-out run outranks an expected-killer check",
            Mutation.Judge(1, "failed A.B (1ms)\n", ["A.B"], timedOut: true).Kind, Verdict.TimedOut);
        Eq("a build error still outranks a timeout",
            Mutation.Judge(1, "error CS0103: nope\n", [], timedOut: true).Kind, Verdict.BuildKilled);
        Eq("without a timeout the verdict is unchanged",
            Mutation.Judge(1, "failed A.B (1ms)\n", [], timedOut: false).Kind, Verdict.Killed);

        // --- Shell.Run's contract ----------------------------------------------------
        // These run real subprocesses; nothing pure can assert this layer.
        //
        // They do NOT guard the cmd.exe quoting choice above. That was tried: a
        // mutant reverting `psi.Arguments` to `ArgumentList` SURVIVED all of them,
        // and no command shape could be found that discriminates the two. Under
        // test-filter-development a surviving mutant rejects the test rather than
        // the other way round, so these are labelled as what they actually verify --
        // that a command runs, that its exit code and both streams come back, and
        // that a fast command is not called a timeout -- and the quoting line is
        // documented as unproven rather than defended by a test that does not test it.
        (int qExit, string qOut, _) = Shell.Run("echo plain", 60);
        Eq("unquoted command runs", qExit, 0);
        True_("unquoted command output is captured", qOut.Contains("plain"));

        (int q2Exit, string q2Out, _) = Shell.Run("echo \"quoted arg\"", 60);
        Eq("a command containing quotes runs", q2Exit, 0);
        True_("a quoted argument reaches the child intact", q2Out.Contains("quoted arg"));

        (_, string q3Out, _) = Shell.Run("echo a \"b c\" d", 60);
        True_("a command with several quoted tokens is not truncated",
            q3Out.Contains("a") && q3Out.Contains("b c") && q3Out.Contains("d"));

        (int q4Exit, _, _) = Shell.Run("exit 7", 60);
        Eq("exit code propagates", q4Exit, 7);

        (_, string q5Out, _) = Shell.Run("echo err 1>&2", 60);
        True_("stderr is captured too", q5Out.Contains("err"));

        (_, _, bool q6Timed) = Shell.Run("echo fast", 60);
        True_("a fast command is not reported as timed out", !q6Timed);

        // --- a command that never ran is NOT a kill --------------------------------
        // The field report that produced these: a --test-cmd whose path would not
        // resolve under cmd.exe never started, the build succeeded, the shell returned
        // 1, and the harness printed MUTANT KILLED and exited 0. SKILL.md promises the
        // opposite in as many words. CHANGELOG 3.3.0 already claimed this fixed; what
        // shipped was the cmd.exe quoting line, which its own comment calls unproven.
        // This is the guard that actually closes it.
        string neverRan =
            "Build succeeded.\n    0 Error(s)\n"
            + "'tests' is not recognized as an internal or external command,\n"
            + "operable program or batch file.\n";

        Eq("the reported transcript is a command failure, not a kill",
            Mutation.Judge(1, neverRan, []).Kind, Verdict.CommandFailed);
        Eq("...and it stays one when an expected killer was named",
            Mutation.Judge(1, neverRan, ["Wanted"]).Kind, Verdict.CommandFailed);
        True_("a command failure quotes the line that triggered it",
            Mutation.Judge(1, neverRan, []).Evidence.Count == 1);
        True_("a command failure names no tests and claims no missing killer",
            Mutation.Judge(1, neverRan, ["W"]).FailedTests.Count == 0
                && Mutation.Judge(1, neverRan, ["W"]).MissingExpected.Count == 0);
        Eq("cmd.exe's 9009 is a command failure",
            Mutation.Judge(9009, "nothing useful\n", []).Kind, Verdict.CommandFailed);
        Eq("sh's 127 is a command failure",
            Mutation.Judge(127, "nothing useful\n", []).Kind, Verdict.CommandFailed);
        Eq("sh's not-found line is a command failure",
            Mutation.Judge(1, "/bin/sh: 1: tests: not found\n", []).Kind, Verdict.CommandFailed);
        Eq("bash's command-not-found line is a command failure",
            Mutation.Judge(1, "bash: line 1: tests: command not found\n", []).Kind, Verdict.CommandFailed);
        Eq("cmd's missing-path line is a command failure",
            Mutation.Judge(1, "The system cannot find the path specified\n", []).Kind, Verdict.CommandFailed);

        // A run that executed ZERO tests never ran either, and is likelier in this
        // harness's own workflow than an unresolvable path: --filter FullyQualifiedName~Ordreing
        // matches nothing and exits non-zero. Not position-constrained -- runners print
        // this in a summary with more output after it.
        Eq("a filter matching nothing is a command failure",
            Mutation.Judge(1, "No test is available in C:\\r\\Tests.dll\nDone.\nBye.\nEnd.\n", []).Kind,
            Verdict.CommandFailed);
        Eq("zero tests ran is a command failure",
            Mutation.Judge(1, "Zero tests ran\nsummary\nfooter\ntrailer\n", []).Kind, Verdict.CommandFailed);
        Eq("MTP's found-0-test is a command failure",
            Mutation.Judge(1, "found 0 test suites\nsummary\nfooter\ntrailer\n", []).Kind, Verdict.CommandFailed);

        // A never-run command CAN exit zero: `cmd /s /c "tests.exe & echo done"`.
        // Scoring that SURVIVED is worse than scoring it anything else, because a
        // surviving mutant REJECTS the test -- it sends someone to strengthen an
        // assertion that never executed.
        Eq("a green exit with an unresolved command is NOT a survival",
            Mutation.Judge(0, "/bin/sh: 1: tests: not found\n", []).Kind, Verdict.CommandFailed);
        Eq("a green exit from a suite that really passed is still a survival",
            Mutation.Judge(0, "passed A.B (1ms)\n", []).Kind, Verdict.Survived);

        // --- ...and the far harder half: what must NOT be a command failure ---------
        // Every marker above is a string the code under test can itself emit, and the
        // path/process mutants in seeding-mutants.md's catalogue are the ones most
        // likely to make it do so. Scoring those inconclusive would hide a real kill.
        Eq("a named failure whose message mentions a missing path is still a kill",
            Mutation.Judge(1, "failed Foo.Bar (12ms)\n  The system cannot find the path specified.\n", []).Kind,
            Verdict.Killed);
        Eq("an xUnit failure whose SUT could not start a process is still a kill",
            Mutation.Judge(1, "  Foo.Tests.LaunchesHelper [FAIL]\n  Win32Exception: The system cannot find the path specified.\n", []).Kind,
            Verdict.Killed);
        Eq("an NUnit failure whose SUT shelled out to a bad exe is still a kill",
            Mutation.Judge(1, "1) Failed : Foo.Tests.Bar\n'helper' is not recognized as an internal or external command\n", []).Kind,
            Verdict.Killed);
        Eq("an assertion message ending in 'not found' is not a shell marker",
            Mutation.Judge(1, "Assert.IsTrue failed. key 'alpha': not found\n", []).Kind, Verdict.Killed);
        Eq("a test asserting on the phrase 'command not found' does not fake a command failure",
            Mutation.Judge(1, "Expected stderr to contain 'command not found' but it was empty\n", []).Kind,
            Verdict.Killed);
        Eq("a node ENOENT raised by the mutant is a kill, not a command failure",
            Mutation.Judge(1, "  1) resolves the fixture:\n  ENOENT: no such file or directory, open 'x.json'\n", []).Kind,
            Verdict.Killed);
        Eq("exit 127 from a runner that DID name failures is a kill",
            Mutation.Judge(127, "failed A.B (1ms)\n", []).Kind, Verdict.Killed);
        Eq("a suite reporting 0 tests found WHILE naming a failure is a kill",
            Mutation.Judge(1, "found 0 test suites\nfailed A.B (1ms)\n", []).Kind, Verdict.Killed);
        Eq("a shell marker buried mid-transcript is not the run's outcome",
            Mutation.Judge(1, "'x' is not recognized as an internal or external command\nRan 4 tests\n1 failed\nsee above\n", []).Kind,
            Verdict.Killed);
        Eq("a build error still outranks a command failure",
            Mutation.Judge(9009, "error CS0103: nope\n", []).Kind, Verdict.BuildKilled);
        Eq("a timeout still outranks a command failure",
            Mutation.Judge(9009, "nothing useful\n", [], timedOut: true).Kind, Verdict.TimedOut);

        // --- MSTest under MTP via `dotnet test` ------------------------------------
        // The format the parser did not know, which made --expect-killed-by unusable
        // on the whole of a real project. The assertion body is emitted as further
        // "error test failed:" lines WITHOUT the "(<duration>):" group -- that trailing
        // colon, not the duration, is what excludes them.
        True_("MSTest under MTP via dotnet test",
            Mutation.FailedTests(
                "C:\\r\\ReviewScorerTests.cs(80): error test failed: Score_FindingFar_DoesNotAutoMatch (85ms): Expected score.Hits to be empty  [C:\\r\\Tests.csproj]")
                .SequenceEqual(["Score_FindingFar_DoesNotAutoMatch"]));
        True_("the assertion body continuation lines are not test names",
            Mutation.FailedTests(
                "T.cs(80): error test failed: Score_X (85ms): Expected\n"
                + "T.cs(80): error test failed: {\n"
                + "T.cs(80): error test failed:     DefectId = \"D1\",\n"
                + "T.cs(80): error test failed: }\n")
                .SequenceEqual(["Score_X"]));
        True_("a test slower than a second still parses",
            Mutation.FailedTests("T.cs(9): error test failed: SlowOne (1.2s): boom").SequenceEqual(["SlowOne"]));
        True_("a whole-second duration still parses",
            Mutation.FailedTests("T.cs(9): error test failed: SlowTwo (2s): boom").SequenceEqual(["SlowTwo"]));
        True_("a parameterised MSTest name keeps its arguments",
            Mutation.FailedTests("T.cs(9): error test failed: Adds (a: 1, b: 2) (3ms): boom")
                .SequenceEqual(["Adds (a: 1, b: 2)"]));
        True_("the project tag is not part of the name",
            Mutation.FailedTests("T.cs(9): error test failed: Foo (5ms): boom  [C:\\r\\Tests.csproj]")
                .SequenceEqual(["Foo"]));
        True_("an MTP line without a duration is a continuation, not a name",
            Mutation.FailedTests("T.cs(80): error test failed: {").Count == 0);
        True_("two failures in MTP form are both captured",
            Mutation.FailedTests("T.cs(1): error test failed: A (1ms): x\nT.cs(2): error test failed: B (2ms): y\n").Count == 2);
        Eq("an MTP red run attributes to the right test",
            Mutation.Judge(1, "T.cs(80): error test failed: Score_X (85ms): boom\n", ["Score_X"]).Kind, Verdict.Killed);
        Eq("an MTP red run rejects the wrong expected killer",
            Mutation.Judge(1, "T.cs(80): error test failed: Score_X (85ms): boom\n", ["Score_Y"]).Kind,
            Verdict.KilledByWrongTest);
        True_("the direct-binary MTP shape still parses",
            Mutation.FailedTests("failed Score_X (85ms)").SequenceEqual(["Score_X"]));

        // xUnit and NUnit: unparsed names used to be a harmless documented limitation.
        // They now gate a verdict, so the two common .NET runners not already covered
        // stop being optional.
        True_("xUnit failure line",
            Mutation.FailedTests("    Foo.Tests.Bar [FAIL]").SequenceEqual(["Foo.Tests.Bar"]));
        True_("NUnit failure line",
            Mutation.FailedTests("1) Failed : Foo.Tests.Bar").SequenceEqual(["Foo.Tests.Bar"]));
        True_("the MTP pattern does not swallow a VSTest line",
            Mutation.FailedTests("  Failed Foo.Bar [12 ms]").SequenceEqual(["Foo.Bar"]));
        True_("the MTP pattern does not swallow the generic FAIL shape",
            Mutation.FailedTests("FAIL  tab inside a string literal").SequenceEqual(["tab inside a string literal"]));

        // --- an assertion message is not a build error -----------------------------
        // Live the moment MTP failure lines gained a diagnostic-shaped prefix: a test
        // asserting on an analyzer code would otherwise be scored BUILD-KILLED, and a
        // genuine kill reported as "no test ever ran". This battery asserts on [SA1028]
        // itself a few cases above, so the hazard is not hypothetical here.
        True_("an MTP failure line is not a build error",
            Mutation.BuildErrors("T.cs(80): error test failed: Foo (5ms): boom").Count == 0);
        True_("a test asserting on an analyzer code is not a build error",
            Mutation.BuildErrors("T.cs(80): error test failed: Foo (5ms): expected error SA1028 got none").Count == 0);
        True_("a diagnostic code without its colon is prose, not a build error",
            Mutation.BuildErrors("the fixture expects error CS0103 to be raised").Count == 0);
        True_("NuGet restore failure detected", Mutation.BuildErrors("error NU1101: Unable to find package").Count == 1);
        True_("MSBuild project failure detected",
            Mutation.BuildErrors("MSBUILD : error MSB1009: Project file does not exist.").Count == 1);
        True_("SDK failure detected", Mutation.BuildErrors("error NETSDK1004: Assets file not found").Count == 1);
        Eq("a NuGet restore failure is build-killed, not a kill",
            Mutation.Judge(1, "error NU1101: Unable to find package\n", []).Kind, Verdict.BuildKilled);

        // --- the verdict is the single source of truth ------------------------------
        // Defect 3: Describe keyed off the int exit code, and the exit code was assigned
        // in seven places inside the console switch. Two representations of one fact, so
        // they disagreed -- a killed mutant recorded itself as SURVIVED in the block the
        // docs tell you to paste into a commit.
        True_("only a kill exits zero",
            Enum.GetValues<Verdict>().Where(v => Report.ExitCode(v) == 0).SequenceEqual([Verdict.Killed]));
        True_("every verdict has an exit code and a record phrase",
            Enum.GetValues<Verdict>().All(v => Report.ExitCode(v) >= 0 && !string.IsNullOrWhiteSpace(Report.Describe(v))));
        Eq("every verdict has a DISTINCT record phrase",
            Enum.GetValues<Verdict>().Select(Report.Describe).Distinct().Count(), Enum.GetValues<Verdict>().Length);
        Eq("an unattributed kill does not record itself as SURVIVED",
            Report.Describe(Verdict.Unattributed).Contains("SURVIVED"), false);
        Eq("a wrong-test kill does not record itself as SURVIVED",
            Report.Describe(Verdict.KilledByWrongTest).Contains("SURVIVED"), false);
        Eq("only a real survival records itself as SURVIVED",
            Enum.GetValues<Verdict>().Count(v => Report.Describe(v).Contains("SURVIVED")), 1);
        Eq("a command failure records itself as inconclusive",
            Report.Describe(Verdict.CommandFailed).Contains("inconclusive"), true);
        True_("no record phrase contains a newline (it is pasted into a PR body)",
            Enum.GetValues<Verdict>().All(v => !Report.Describe(v).Contains('\n')));
        Eq("a command failure exits 5", Report.ExitCode(Verdict.CommandFailed), 5);
        Eq("an unattributed kill exits 6", Report.ExitCode(Verdict.Unattributed), 6);
        Eq("a wrong-test kill exits 7", Report.ExitCode(Verdict.KilledByWrongTest), 7);

        // --- display ---------------------------------------------------------------
        Eq("truncate escapes newlines", Mutation.Truncate("a\nb"), "a\\nb");
        Eq("truncate escapes tabs", Mutation.Truncate("a\tb"), "a\\tb");
        True_("truncate caps at 100", Mutation.Truncate(new string('x', 200)).Length == 100);
        True_("truncate leaves short text alone", Mutation.Truncate("short") == "short");

        foreach (string f in failures)
        {
            Console.Error.WriteLine($"FAIL  {f}");
        }

        Console.WriteLine($"selftest: {count - failures.Count}/{count} passed");
        return failures.Count == 0 ? 0 : 1;
    }
}
