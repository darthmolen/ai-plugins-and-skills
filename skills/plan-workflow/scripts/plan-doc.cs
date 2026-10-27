using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

#pragma warning disable IL2026 // single-file script, not trimmed
#pragma warning disable IL3050 // single-file script, not AOT-compiled

// -----------------------------------------------------------------------------
// plan-doc.cs — the planning corpus's frontmatter contract, made enforceable.
// -----------------------------------------------------------------------------
// Usage:
//   dotnet run plan-doc.cs -- validate [--path planning] [--specs <dir>]...
//   dotnet run plan-doc.cs -- close --doc <path> --to <status> [options]
//   dotnet run plan-doc.cs -- spec-table --specs <dir>... [--path planning] --check|--fix
//   dotnet run plan-doc.cs -- wave-table [--path planning] --check|--fix
//   dotnet run plan-doc.cs -- --selftest
//
// THREE VERBS, ONE PARSER, ON PURPOSE. `close` prevents one class of
// status/directory drift; `validate` reports every class of it. They must agree
// about what the contract says, so they share the tables in Contract below rather
// than each growing their own reading of plan-writing-syntax.
//
// .NET file-based apps compile a single file -- there is no #:include -- so two
// scripts sharing this parser would need a real project or a copy of it. One
// script with two verbs shares it by construction.
//
// spec-table:
//   system-spec. A system spec's status table is generated from its feature lines and
//   from the plans whose delivers: names each feature. Resolving delivers: needs
//   plans and specs read by the same parser, which is why this is a third verb
//   here rather than a script in the system-spec skill.
//
// close:
//   plan-workflow says it plainly: "A move is two edits, not one" -- git mv the
//   file AND change status: to match. Doing it by hand is two steps with nothing
//   binding them. This makes the pair atomic and refuses the cases that produce a
//   document that lies about itself.
//
// validate:
//   plan-writing-syntax: "frontmatter with no validator is markdown with more
//   punctuation." Three skills already reference this script conditionally.
//
// Exit codes:
//   0  ok      1  violations found / refused      2  usage error
// -----------------------------------------------------------------------------

if (args.Length == 0)
{
    Usage();
    return 2;
}

switch (args[0])
{
    case "--selftest":
        return SelfTest.Run();
    case "validate":
        return Validate(args[1..]);
    case "close":
        return Close(args[1..]);
    case "spec-table":
        return SpecTable(args[1..]);
    case "wave-table":
        return WaveTableVerb(args[1..]);
    case "-h" or "--help":
        Usage();
        return 0;
    default:
        Console.Error.WriteLine($"unknown verb: {args[0]}");
        Usage();
        return 2;
}

// -----------------------------------------------------------------------------
// VALIDATE
// -----------------------------------------------------------------------------

static int Validate(string[] argv)
{
    string root = "planning";
    var specDirs = new List<string>();
    for (int i = 0; i < argv.Length; i++)
    {
        if (argv[i] == "--path" && i + 1 < argv.Length)
        {
            root = argv[++i];
        }
        else if (argv[i] == "--specs" && i + 1 < argv.Length)
        {
            specDirs.Add(argv[++i]);
        }
        else
        {
            Console.Error.WriteLine($"unknown argument: {argv[i]}");
            return 2;
        }
    }

    if (!Directory.Exists(root))
    {
        Console.Error.WriteLine($"path not found: {root}");
        return 2;
    }

    if (specDirs.FirstOrDefault(d => !Directory.Exists(d)) is string missingSpecs)
    {
        Console.Error.WriteLine($"path not found: {missingSpecs}");
        return 2;
    }

    List<PlanDoc> docs = LoadPlans(root);
    List<Finding> findings = [.. Validator.Check(docs)];

    string signPath = Path.Combine(root, WaveSign.FileName);
    if (WaveSign.Check(File.Exists(signPath) ? File.ReadAllText(signPath) : null, docs) is Finding signFinding)
    {
        findings.Add(signFinding);
    }

    // Specs are checked only when asked for: they live beside the code (docs/ or
    // documentation/), not under planning/, and most repositories have none.
    if (specDirs.Count > 0)
    {
        List<SpecDoc> specs = LoadSpecs(specDirs);
        findings.AddRange(Specs.Check(specs, docs));
        Console.WriteLine($"specs: {specs.Count} checked under {string.Join(", ", specDirs)}");
    }

    foreach (Finding f in findings.OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Rule, StringComparer.Ordinal))
    {
        Console.WriteLine($"{f.File}: {f.Rule} — {f.Detail}");
    }

    Console.WriteLine();
    if (findings.Count == 0)
    {
        Console.WriteLine($"OK — {docs.Count} document(s) conform to the frontmatter contract.");
        return 0;
    }

    Console.WriteLine($"{findings.Count} violation(s) across {findings.Select(f => f.File).Distinct().Count()} document(s) of {docs.Count} checked.");
    Console.WriteLine("See plan-writing-syntax for the contract; this script is the authority where they disagree.");
    return 1;
}

static List<PlanDoc> LoadPlans(string root)
{
    var docs = new List<PlanDoc>();
    foreach (string path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
                 .OrderBy(p => p, StringComparer.Ordinal))
    {
        string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
        if (Contract.IsExempt(rel))
        {
            continue;
        }

        string text = File.ReadAllText(path);
        (var fields, _, string body, bool found) = Fm.Parse(text);
        docs.Add(new PlanDoc(path, rel, fields, body, found));
    }

    return docs;
}

// A spec is any markdown file under the given directories whose frontmatter says
// kind: spec. Discovery is by kind, not location, so docs/ and documentation/ both work.
static List<SpecDoc> LoadSpecs(IEnumerable<string> dirs)
{
    var specs = new List<SpecDoc>();
    foreach (string path in dirs.SelectMany(d => Directory.EnumerateFiles(d, "*.md", SearchOption.AllDirectories))
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .OrderBy(p => p, StringComparer.Ordinal))
    {
        string rel = Path.GetRelativePath(Environment.CurrentDirectory, path).Replace('\\', '/');
        if (Specs.Load(path, rel, File.ReadAllText(path)) is SpecDoc spec)
        {
            specs.Add(spec);
        }
    }

    return specs;
}

// -----------------------------------------------------------------------------
// SPEC-TABLE — regenerate each spec's status table (system-spec)
// -----------------------------------------------------------------------------

static int SpecTable(string[] argv)
{
    string root = "planning";
    var specDirs = new List<string>();
    string? mode = null;
    for (int i = 0; i < argv.Length; i++)
    {
        switch (argv[i])
        {
            case "--path" when i + 1 < argv.Length: root = argv[++i]; break;
            case "--specs" when i + 1 < argv.Length: specDirs.Add(argv[++i]); break;
            case "--check" or "--fix": mode = argv[i]; break;
            default:
                Console.Error.WriteLine($"unknown argument: {argv[i]}");
                return 2;
        }
    }

    if (mode is null || specDirs.Count == 0)
    {
        Console.Error.WriteLine("spec-table needs --specs <dir> and one of --check or --fix");
        return 2;
    }

    foreach (string dir in specDirs.Append(root).Where(d => !Directory.Exists(d)))
    {
        Console.Error.WriteLine($"path not found: {dir}");
        return 2;
    }

    List<PlanDoc> plans = LoadPlans(root);
    int stale = 0;
    foreach (SpecDoc spec in LoadSpecs(specDirs))
    {
        string table = Specs.RenderTable(spec, plans);
        string? current = Specs.CurrentTable(spec.Text);
        if (current is null)
        {
            Console.WriteLine($"{spec.Rel}: SPEC-NO-TABLE — add {Specs.Begin} and {Specs.End} where the table belongs");
            stale++;
        }
        else if (current == table)
        {
            Console.WriteLine($"{spec.Rel}: current");
        }
        else if (mode == "--check")
        {
            Console.WriteLine($"{spec.Rel}: SPEC-TABLE-STALE");
            stale++;
        }
        else
        {
            byte[] original = File.ReadAllBytes(spec.Path);
            bool bom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
            File.WriteAllText(spec.Path, Specs.ApplyTable(spec.Text, table)!, new UTF8Encoding(bom));
            Console.WriteLine($"{spec.Rel}: table regenerated");
        }
    }

    return stale == 0 ? 0 : 1;
}

// -----------------------------------------------------------------------------
// WAVE-TABLE — regenerate each wave document's plan table from the wave-<n>_ prefix
// -----------------------------------------------------------------------------

static int WaveTableVerb(string[] argv)
{
    string root = "planning";
    string? mode = null;
    for (int i = 0; i < argv.Length; i++)
    {
        switch (argv[i])
        {
            case "--path" when i + 1 < argv.Length: root = argv[++i]; break;
            case "--check" or "--fix": mode = argv[i]; break;
            default:
                Console.Error.WriteLine($"unknown argument: {argv[i]}");
                return 2;
        }
    }

    if (mode is null)
    {
        Console.Error.WriteLine("wave-table needs one of --check or --fix");
        return 2;
    }

    if (!Directory.Exists(root))
    {
        Console.Error.WriteLine($"path not found: {root}");
        return 2;
    }

    List<PlanDoc> docs = LoadPlans(root);
    int stale = 0;
    foreach (PlanDoc wave in docs.Where(WaveTable.HostsTable))
    {
        string table = WaveTable.Render(wave, docs);
        string? current = WaveTable.Current(wave.Body);
        if (current is null)
        {
            Console.WriteLine($"{wave.Rel}: no table — add {WaveTable.Begin} and {WaveTable.End} where it belongs");
        }
        else if (current == table)
        {
            Console.WriteLine($"{wave.Rel}: current");
        }
        else if (mode == "--check")
        {
            Console.WriteLine($"{wave.Rel}: WAVE-TABLE-STALE");
            stale++;
        }
        else
        {
            byte[] original = File.ReadAllBytes(wave.Path);
            bool bom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
            File.WriteAllText(wave.Path, WaveTable.Apply(File.ReadAllText(wave.Path), table)!, new UTF8Encoding(bom));
            Console.WriteLine($"{wave.Rel}: table regenerated");
        }
    }

    // The sign follows the tables: up while a wave is open, down when none is.
    string signPath = Path.Combine(root, WaveSign.FileName);
    string? currentSign = File.Exists(signPath) ? File.ReadAllText(signPath) : null;
    string? sign = WaveSign.Render(docs);
    if (WaveSign.Check(currentSign, docs) is null)
    {
        Console.WriteLine(sign is null ? $"{WaveSign.FileName}: no wave open, no sign" : $"{WaveSign.FileName}: current");
    }
    else if (mode == "--check")
    {
        Console.WriteLine($"{WaveSign.FileName}: WAVE-README-STALE");
        stale++;
    }
    else if (sign is null)
    {
        File.Delete(signPath);
        Console.WriteLine($"{WaveSign.FileName}: no wave open; sign taken down");
    }
    else
    {
        File.WriteAllText(signPath, sign, new UTF8Encoding(false));
        Console.WriteLine($"{WaveSign.FileName}: sign regenerated");
    }

    return stale == 0 ? 0 : 1;
}

// -----------------------------------------------------------------------------
// CLOSE
// -----------------------------------------------------------------------------

static int Close(string[] argv)
{
    string? doc = null, to = null, statusBlock = null, blockedOn = null, reason = null, reasonTarget = null;
    string? promotedTo = null, decidedBy = null, decidedOn = null;
    string completed = DateTime.Now.ToString("yyyy-MM-dd");
    bool dryRun = false;

    for (int i = 0; i < argv.Length; i++)
    {
        switch (argv[i])
        {
            case "--doc" when i + 1 < argv.Length: doc = argv[++i]; break;
            case "--to" when i + 1 < argv.Length: to = argv[++i]; break;
            case "--status-block" when i + 1 < argv.Length: statusBlock = argv[++i]; break;
            case "--completed" when i + 1 < argv.Length: completed = argv[++i]; break;
            case "--blocked-on" when i + 1 < argv.Length: blockedOn = argv[++i]; break;
            case "--reason" when i + 1 < argv.Length: reason = argv[++i]; break;
            case "--reason-target" when i + 1 < argv.Length: reasonTarget = argv[++i]; break;
            case "--promoted-to" when i + 1 < argv.Length: promotedTo = argv[++i]; break;
            case "--decided-by" when i + 1 < argv.Length: decidedBy = argv[++i]; break;
            case "--decided-on" when i + 1 < argv.Length: decidedOn = argv[++i]; break;
            case "--dry-run": dryRun = true; break;
            default:
                Console.Error.WriteLine($"unknown argument: {argv[i]}");
                return 2;
        }
    }

    if (doc is null || to is null)
    {
        Console.Error.WriteLine("--doc and --to are required.");
        Usage();
        return 2;
    }

    if (!File.Exists(doc))
    {
        Console.Error.WriteLine($"plan document not found: {doc}");
        return 2;
    }

    string text = File.ReadAllText(doc);
    (var fields, string rawFm, string body, bool found) = Fm.Parse(text);

    if (!found)
    {
        Console.Error.WriteLine($"no frontmatter block in {doc}. See plan-writing-syntax.");
        return 1;
    }

    string kind = fields.GetValueOrDefault("kind", "plan");
    string? current = fields.GetValueOrDefault("status");
    if (current is null)
    {
        Console.Error.WriteLine($"no status: field in {doc}.");
        return 1;
    }

    if (!Contract.StatusesFor(kind).Contains(to))
    {
        Console.Error.WriteLine($"'{to}' is not a valid status for kind '{kind}'. Valid: {string.Join(", ", Contract.StatusesFor(kind))}");
        return 2;
    }

    // The directory IS the status. If they already disagree, something upstream is
    // broken and this is not the tool to paper over it.
    string currentDir = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(doc))!);
    string planningRoot = Path.GetDirectoryName(Path.GetFullPath(doc))!;
    if (currentDir != "planning")
    {
        planningRoot = Path.GetDirectoryName(planningRoot)!;
    }

    string[] allowedNow = Contract.DirectoriesFor(kind, current);
    if (!allowedNow.Any(d => DirLeaf(d) == currentDir))
    {
        Console.Error.WriteLine(
            $"{doc} claims status '{current}' but sits in '{currentDir}/'. Expected {string.Join(" or ", allowedNow)}. Fix that disagreement first.");
        return 1;
    }

    if (Regex.IsMatch(body, @"(?m)^## Status\s*$"))
    {
        Console.Error.WriteLine($"{doc} already has a '## Status' section. Refusing to append a second.");
        return 1;
    }

    // Required-field gate: "A status that names nothing says nothing."
    //
    // Driven by Contract.RequiredFields, NOT by a switch over statuses. A switch is
    // how close and validate came to disagree: close had no case for a promoted
    // stub, so it wrote a document that validate immediately rejected for a missing
    // promoted_to:. Reading the same table means a status added to the contract
    // cannot be silently unsupported here.
    var supplied = new Dictionary<string, string>(StringComparer.Ordinal);

    if (to == "completed")
    {
        supplied["completed"] = completed;
    }

    foreach ((string flag, string? value) in new[]
             {
                 ("blocked_on", blockedOn),
                 ("promoted_to", promotedTo),
                 ("decided_by", decidedBy),
                 ("decided_on", decidedOn),
             })
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            supplied[flag] = value!;
        }
    }

    if (to == "not-implemented")
    {
        if (reason is null || !Contract.ReasonFields.ContainsKey(reason))
        {
            Console.Error.WriteLine($"not-implemented requires --reason from: {string.Join(", ", Contract.ReasonFields.Keys)}");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(reasonTarget))
        {
            Console.Error.WriteLine($"reason '{reason}' requires --reason-target (a NAME, not a path).");
            return 2;
        }

        supplied["reason"] = reason;
        supplied[Contract.ReasonFields[reason]] = reasonTarget;
    }

    // Dates are ISO, checked against the same function validate uses -- close must not
    // write a document its own validate verb would then reject.
    foreach (string field in Contract.DateFields)
    {
        if (supplied.TryGetValue(field, out string? when) && !Contract.IsIsoDate(when))
        {
            Console.Error.WriteLine($"--{field.Replace('_', '-')} must be ISO YYYY-MM-DD: {when}");
            return 2;
        }
    }

    // References are names, never paths -- checked on every reference field close can
    // set, not only on --reason-target.
    foreach (string field in Contract.ReferenceFields.Keys)
    {
        if (supplied.TryGetValue(field, out string? v) && Names.LooksLikePath(v))
        {
            Console.Error.WriteLine($"--{field.Replace('_', '-')} must be a name, not a path: {v}");
            Console.Error.WriteLine("plan-writing-syntax: a stored path is wrong the moment its target moves.");
            return 2;
        }
    }

    foreach (string req in Contract.RequiredFields(kind, to))
    {
        string[] alternatives = req.Split('|');
        if (!alternatives.Any(a => supplied.ContainsKey(a)))
        {
            Console.Error.WriteLine(
                $"{kind}/{to} requires {string.Join(" or ", alternatives.Select(a => "--" + a.Replace('_', '-')))}.");
            Console.Error.WriteLine(CloseFields.Rationale(alternatives[0]));
            return 2;
        }
    }

    var extra = supplied.Select(kv => (Field: kv.Key, Value: kv.Value)).ToList();

    string newFm = Fm.Rewrite(rawFm, to, extra);
    string outText = $"---\n{newFm}\n---\n{body}";

    if (statusBlock is not null)
    {
        if (!File.Exists(statusBlock))
        {
            Console.Error.WriteLine($"status block not found: {statusBlock}");
            return 2;
        }

        string block = File.ReadAllText(statusBlock).Replace("\r\n", "\n");
        outText = outText.TrimEnd('\n') + "\n\n" + block.Trim('\n') + "\n";
    }
    else if (current == "in-progress")
    {
        // The rule is "every departure from in-progress/", so it does not apply to a
        // stub being promoted or closed -- those never sat there.
        Console.Error.WriteLine("WARNING: no --status-block. plan-workflow requires a Status section on every departure from in-progress/.");
    }

    string destDir = Path.Combine(planningRoot, DirLeaf(Contract.DirectoriesFor(kind, to)[0]) == "planning"
        ? string.Empty
        : DirLeaf(Contract.DirectoriesFor(kind, to)[0]));
    string destName = Names.Rename(Path.GetFileName(doc), kind, to);
    string dest = Path.Combine(destDir, destName);

    if (dryRun)
    {
        Console.WriteLine($"status: {current} -> {to}");
        Console.WriteLine($"move:   {doc} -> {dest}");
        Console.WriteLine("--- new frontmatter ---");
        Console.WriteLine(newFm);
        return 0;
    }

    if (!Directory.Exists(destDir))
    {
        Console.Error.WriteLine($"destination directory does not exist: {destDir}");
        return 1;
    }

    // Planning docs in this corpus are CRLF throughout.
    File.WriteAllText(doc, outText.Replace("\n", "\r\n"), new UTF8Encoding(false));

    if (Git("mv", "--", doc, dest) != 0)
    {
        Console.Error.WriteLine($"git mv failed; frontmatter was already rewritten in place at {doc}.");
        return 1;
    }

    // MANDATORY. `git mv` stages the rename using the blob already in the index, so
    // the frontmatter rewrite above -- a working-tree change that was never added --
    // is left UNSTAGED, and a subsequent commit moves the file while committing its
    // OLD content. Observed for real on ai-agents 533ed4e: the document landed in
    // completed/ still saying status: in-progress, which is the precise drift this
    // script exists to prevent, shipped by the script's own move.
    if (Git("add", "--", dest) != 0)
    {
        Console.Error.WriteLine($"git add of {dest} failed; the move is staged but the content edit is not.");
        return 1;
    }

    Console.WriteLine($"status: {current} -> {to}");
    Console.WriteLine($"moved:  {doc} -> {dest}");
    Console.WriteLine("Commit the move and the field together — they are one change.");
    return 0;
}

static string DirLeaf(string d) => d.TrimEnd('/').Split('/').Last();

static int Git(params string[] argv)
{
    var psi = new ProcessStartInfo { FileName = "git", UseShellExecute = false };
    foreach (string a in argv)
    {
        psi.ArgumentList.Add(a);
    }

    using var p = Process.Start(psi)!;
    p.WaitForExit();
    return p.ExitCode;
}

static void Usage()
{
    Console.Error.WriteLine("""
        plan-doc — the planning frontmatter contract, made enforceable

          validate [--path planning] [--specs <dir>]...
                                            report every contract violation; exit 1 on any.
                                            --specs also checks system specs (system-spec)
          spec-table --specs <dir>... [--path planning] --check|--fix
                                            check or regenerate each spec's status table
          wave-table [--path planning] --check|--fix
                                            check or regenerate each wave document's plan table,
                                            and the WAVE-README.md sign while a wave is open
          close --doc <path> --to <status>  rewrite status: and git mv, atomically
                [--status-block <file>] [--completed <date>] [--blocked-on <text>]
                [--reason <r>] [--reason-target <name>] [--promoted-to <name>]
                [--decided-by <who>] [--decided-on <date>] [--dry-run]
          --selftest                        run the built-in battery; exit 0 iff all pass
        """);
}

// -----------------------------------------------------------------------------
// THE CONTRACT — the pure core, shared by both verbs. Tested by --selftest.
// Source: plan-writing-syntax. Where they disagree, that page says this wins.
// -----------------------------------------------------------------------------

record Finding(string File, string Rule, string Detail);

record PlanDoc(string Path, string Rel, Dictionary<string, string> Fields, string Body, bool HasFrontmatter)
{
    public string Kind => this.Fields.GetValueOrDefault("kind", "");

    public string Status => this.Fields.GetValueOrDefault("status", "");

    public string Dir
    {
        get
        {
            int slash = this.Rel.LastIndexOf('/');
            return slash < 0 ? "planning" : this.Rel[..slash];
        }
    }

    public string FileName => this.Rel.Split('/').Last();

    public string Name => Names.FromFileName(this.FileName);

    // The wave a plan belongs to: its wave-<n>_ prefix, or else a well-formed wave: field.
    public string? Wave =>
        this.Kind != "plan" ? null
        : Contract.WaveOf(this.FileName)
          ?? (Contract.IsWaveName(this.Fields.GetValueOrDefault("wave", "")) ? this.Fields["wave"] : null);
}

static class Contract
{
    public static readonly string[] Kinds = ["plan", "stub", "reminder", "review", "wave", "context"];

    // A stub is a baby plan, so the two share one vocabulary (plan-writing-syntax). Folders are
    // status buckets: `backlog` means *deferred, nobody has committed to this yet*,
    // which is as true of a worked-then-outranked plan as of an idea captured
    // mid-session. reminder/review/wave keep their own -- they are different kinds of
    // THING, not lifecycle states of the same one.
    public static readonly string[] BoardStatuses =
        ["backlog", "queued", "in-progress", "completed", "not-implemented", "blocked"];

    static readonly Dictionary<string, string[]> Vocab = new(StringComparer.Ordinal)
    {
        ["plan"] = BoardStatuses,
        ["stub"] = BoardStatuses,
        ["reminder"] = ["open", "done", "dropped"],
        ["review"] = ["open", "done"],
        ["wave"] = ["open", "done"],
        ["context"] = ["open", "done"],
    };

    // The board's statuses, keyed by status ALONE -- a plan and a stub of the same
    // status live in the same place, which is the whole stubs-as-baby-plans rule in one table.
    static readonly Dictionary<string, string[]> BoardDirs = new(StringComparer.Ordinal)
    {
        ["backlog"] = ["backlog"],
        ["queued"] = ["planning"],
        ["in-progress"] = ["in-progress"],
        ["completed"] = ["completed"],
        ["not-implemented"] = ["not-implemented"],
        ["blocked"] = ["blocked"],
    };

    // kind/status -> the directories it may legally sit in, relative to planning/,
    // for the kinds that are not on the board.
    static readonly Dictionary<string, string[]> Dirs = new(StringComparer.Ordinal)
    {
        ["reminder/open"] = ["reminders"],

        // A closed reminder has two homes on purpose: a tool closes it in place, a
        // person files it afterwards. Demanding the move at close time would fail
        // the board the moment anyone used the button.
        ["reminder/done"] = ["reminders/completed", "reminders"],
        ["reminder/dropped"] = ["reminders/completed", "reminders"],
        // The review pipeline moves an open review needs-review/ -> in-progress/ ->
        // reviewed/ (plan-send-review, plan-intake-review); all three are status open.
        ["review/open"] = ["needs-review", "needs-review/in-progress", "needs-review/reviewed"],
        ["review/done"] = ["needs-review/completed"],
        // A wave may also sit in its own folder under waves/ (`waves/*`, one level), so
        // a wave's documents and prompts can be filed together as a unit.
        ["wave/open"] = ["waves", "waves/*"],
        ["wave/done"] = ["waves", "waves/*"],

        // The context every wave's sessions read first outlives any one wave, so it
        // sits at the top of waves/ only.
        ["context/open"] = ["waves"],
        ["context/done"] = ["waves"],
    };

    public static bool OnTheBoard(string kind) =>
        kind is "plan" or "stub";

    // A prefix names the document's KIND and is stable for its life. It used to carry
    // status (feature_ -> promoted_ -> closed_), which renamed the file on every move
    // and forced the "a name is the filename minus its status prefix" rule. Making a stub a baby plan
    // moved the job to kind, where kind: was already doing it.
    static readonly Dictionary<string, string> Prefixes = new(StringComparer.Ordinal)
    {
        ["plan"] = "feature_",
        ["stub"] = "stub_",
    };

    // A plan may say more than feature_ does: bug_ marks a fix, and wave-<n>_ marks a
    // plan written for wave n, so a wave's plans sort together wherever they sit.
    static readonly (string Kind, string Prefix)[] OtherPrefixes = [("plan", "bug_")];

    static readonly Regex WavePrefix = new(@"^(wave-\d+)_", RegexOptions.CultureInvariant);

    public static bool IsWaveName(string value) => Regex.IsMatch(value, @"^wave-\d+$");

    // spec-tier's three tiers, lower-case like every other frontmatter vocabulary.
    public static readonly string[] Tiers = ["low", "medium", "high"];

    // Optional, but a present field with no value says nothing.
    public static readonly string[] NonEmptyFields = ["author", "repo"];

    public static readonly Dictionary<string, string> ReasonFields = new(StringComparer.Ordinal)
    {
        ["superseded"] = "superseded_by",
        ["folded-in"] = "folded_into",
        ["obsoleted"] = "obsoleted_by",
        ["abandoned"] = "learned",
    };

    // A status that names nothing says nothing. Keyed by status alone for the board:
    // a stub is required to carry no more and no less than a plan of the same status.
    // `promoted_to:` is deliberately NOT here -- promotion IS a stub's completion, and
    // which terminal bucket a stub lands in is judgement, not something to police.
    public static string[] RequiredFields(string kind, string status)
    {
        if (!OnTheBoard(kind))
        {
            return [];
        }

        return status switch
        {
            "completed" => ["completed"],
            "blocked" => ["blocked_on"],
            "not-implemented" => ["reason"],
            _ => [],
        };
    }

    // Reference field -> the kinds it may resolve to.
    public static readonly Dictionary<string, string[]> ReferenceFields = new(StringComparer.Ordinal)
    {
        ["promoted_to"] = ["plan"],
        ["promoted_from"] = ["stub", "plan"],
        ["superseded_by"] = ["plan"],
        ["folded_into"] = ["plan"],
        ["plan"] = ["plan", "stub"],
        ["reviews"] = ["plan", "stub"],
    };

    // Optional, and shape-checked rather than required. They record the one fact
    // status cannot: who made the call, and when. There is no `decision:` enum --
    // that would restate status in a fourth place.
    public static readonly string[] DateFields = ["decided_on"];

    // Shared by validate's BAD-DATE rule and close's gate, on purpose. close writing a
    // document that validate then rejects is the exact defect this script's history
    // records; one function means the two cannot drift apart.
    public static bool IsIsoDate(string value) =>
        Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}$");

    // system-spec: a plan cites a spec feature as <spec name>#<feature slug>. The slug is
    // declared on the spec's feature line, never derived from a heading.
    public const string DeliversField = "delivers";

    public static bool IsDeliversEntry(string value) =>
        Regex.IsMatch(value, @"^[a-z0-9]+(?:[-_.][a-z0-9]+)*#[a-z0-9]+(?:-[a-z0-9]+)*$");

    public static string[] StatusesFor(string kind) =>
        Vocab.TryGetValue(kind, out string[]? v) ? v : [];

    public static string[] DirectoriesFor(string kind, string status)
    {
        if (OnTheBoard(kind))
        {
            return BoardDirs.TryGetValue(status, out string[]? b) ? b : [];
        }

        return Dirs.TryGetValue($"{kind}/{status}", out string[]? d) ? d : [];
    }

    // A directory entry ending in `/*` matches exactly one folder below its stem:
    // `waves/*` accepts `waves/wave-0` but not `waves` or `waves/wave-0/old`.
    public static bool InAllowedDirectory(string[] allowed, string dir)
    {
        foreach (string a in allowed)
        {
            if (a.EndsWith("/*", StringComparison.Ordinal))
            {
                string stem = a[..^1];
                if (dir.StartsWith(stem, StringComparison.Ordinal) && dir.Length > stem.Length && !dir[stem.Length..].Contains('/'))
                {
                    return true;
                }
            }
            else if (a == dir)
            {
                return true;
            }
        }

        return false;
    }

    public static string PrefixFor(string kind) =>
        Prefixes.GetValueOrDefault(kind, "");

    public static string? WaveOf(string fileName)
    {
        Match m = WavePrefix.Match(fileName);
        return m.Success ? m.Groups[1].Value : null;
    }

    // A stub MUST carry its prefix. It shares every directory with plans now, so the
    // prefix is the only thing telling the two apart at a glance -- which is the job
    // the directory used to do and no longer does.
    //
    // A plan need not: a MISSING prefix is a lost affordance, not a lie. Documents
    // predating the convention (phase_, or a plain descriptive name) read perfectly
    // well without it, and renaming a completed document to satisfy a new rule is
    // precisely what "the name is the identity" forbids.
    public static bool PrefixRequired(string kind) => kind == "stub";

    // A prefix belonging to ANOTHER kind, on the other hand, is a lie about what the
    // document is, and lies are what this validator is for.
    public static string? ForeignPrefix(string kind, string fileName)
    {
        foreach ((string owner, string prefix) in Prefixes.Select(p => (p.Key, p.Value)).Concat(OtherPrefixes))
        {
            if (owner != kind && fileName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return prefix;
            }
        }

        // The wave document itself is named wave-<n>_<date>.md, so it may carry it too.
        string? wave = WaveOf(fileName);
        return kind is not ("plan" or "wave") && wave is not null ? wave + "_" : null;
    }

    // README.md is signage, not a document of the corpus, and so is the generated
    // WAVE-README.md at the board's root (WaveSign). Everything under
    // evidence/ is an artifact a plan produced and its Status block cites -- an
    // output, not a document, and some are not markdown at all.
    public static bool IsExempt(string rel)
    {
        string r = rel.Replace('\\', '/');
        return r.Split('/').Last().Equals("README.md", StringComparison.OrdinalIgnoreCase)
            || r == WaveSign.FileName
            || r.StartsWith("evidence/", StringComparison.Ordinal)
            || r.Contains("/evidence/", StringComparison.Ordinal);
    }
}

static class CloseFields
{
    // Every frontmatter field close knows how to set, and why the contract wants it.
    // Kept beside Contract.RequiredFields so --selftest can assert the two agree:
    // a required field close cannot supply is a status close cannot legally produce.
    static readonly Dictionary<string, string> Why = new(StringComparer.Ordinal)
    {
        ["completed"] = "the date is the thing completed/ is claiming.",
        ["blocked_on"] = "a blocker with no name is one nobody can clear.",
        ["promoted_to"] = "name the plan the stub became; the stub records when the problem was first seen.",
        ["decided_by"] = "status records the lifecycle; nothing else records that a person made the call.",
        ["decided_on"] = "nor when they made it. ISO YYYY-MM-DD.",
        ["reason"] = "a plan that built nothing must say which of the four reasons.",
        ["superseded_by"] = "name the later plan covering this ground.",
        ["folded_into"] = "name the plan that absorbed this one.",
        ["obsoleted_by"] = "say what moved.",
        ["learned"] = "a rejected approach is knowledge; say what was learned.",
    };

    public static bool CanSupply(string field) => Why.ContainsKey(field);

    public static string Rationale(string field) =>
        Why.TryGetValue(field, out string? w) ? "plan-writing-syntax: " + w : string.Empty;
}

static class Names
{
    static readonly string[] KindPrefixes = ["feature_", "bug_", "stub_"];

    // A name is the filename minus its KIND prefix and .md. Only feature_, bug_ and stub_
    // are stripped. A plan's wave-<n>_ stays, so a reference says which wave the plan
    // belongs to. A reminder's verify_ or follow-up_ is a CATEGORY and stays part
    // of the name, and the retired promoted_ / closed_ are no longer special, which is
    // what makes an un-migrated document fail loudly rather than resolve quietly.
    public static string FromFileName(string fileName)
    {
        string n = fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? fileName[..^3] : fileName;
        foreach (string p in KindPrefixes)
        {
            if (n.StartsWith(p, StringComparison.Ordinal))
            {
                return n[p.Length..];
            }
        }

        return n;
    }

    public static bool LooksLikePath(string value) =>
        value.Contains('/') || value.Contains('\\') || value.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    // Kept as a seam, and deliberately an identity function now: kind does not change
    // when a document moves, so neither does its filename. `close` calls it so the
    // guarantee is stated in one place rather than assumed at the call site.
    public static string Rename(string fileName, string kind, string toStatus)
    {
        _ = toStatus;
        string prefix = Contract.PrefixFor(kind);
        return prefix.Length == 0 || !fileName.StartsWith(prefix, StringComparison.Ordinal)
            ? fileName
            : prefix + FromFileName(fileName) + ".md";
    }
}

record SpecFeature(string Slug, string Heading, string Status, bool HasInterim, string Delivers, string ChangeKind, int Line);

record SpecDoc(string Path, string Rel, Dictionary<string, string> Fields, string Text, IReadOnlyList<SpecFeature> Features, IReadOnlyList<int> BadFeatureLines)
{
    public string Name => this.Fields.GetValueOrDefault("name", "");

    public string Stem => System.IO.Path.GetFileNameWithoutExtension(this.Rel);
}

// -----------------------------------------------------------------------------
// WAVES — a wave's plans live on the board, where their directory is their status,
// and are found by the wave-<n>_ prefix. Both the wave document's plan table and the
// WAVE-README.md sign at the board's root are generated from them, so neither can
// go stale.
// -----------------------------------------------------------------------------

static class WaveTable
{
    public const string Begin = "<!-- wave-table:begin -->";

    public const string End = "<!-- wave-table:end -->";

    // The wave document is waves/wave-<n>/wave-<n>_<date>.md. Supporting kind: wave
    // documents in the same folder carry no table.
    public static bool HostsTable(PlanDoc d) =>
        d.Kind == "wave" && Contract.WaveOf(d.FileName) is string wave && d.Dir == "waves/" + wave;

    // The wave document sits two folders below the board's root, hence the default.
    public static string Render(PlanDoc wave, IReadOnlyList<PlanDoc> docs, string linkPrefix = "../../")
    {
        string? n = Contract.WaveOf(wave.FileName);
        var lines = new List<string> { "| Plan | Track | Tier | Repo | Status |", "| --- | --- | --- | --- | --- |" };
        foreach (PlanDoc p in docs.Where(p => n is not null && p.Wave == n).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            string F(string key) => p.Fields.GetValueOrDefault(key, "");
            lines.Add($"| [`{p.Name}`]({linkPrefix}{p.Rel}) | {F("track")} | {F("tier")} | {F("repo")} | {p.Status} |");
        }

        return string.Join("\n", lines);
    }

    public static string? Current(string text) => Specs.CurrentTable(text, Begin, End);

    public static string? Apply(string text, string table) => Specs.ApplyTable(text, table, Begin, End);
}

static class WaveSign
{
    public const string FileName = "WAVE-README.md";

    // The sign is up while a wave that opted into the generated table is open, and down
    // otherwise. Its presence at the board's root is what says "we are in a wave".
    public static string? Render(IReadOnlyList<PlanDoc> docs)
    {
        PlanDoc[] open = [.. docs
            .Where(d => WaveTable.HostsTable(d) && d.Status == "open" && WaveTable.Current(d.Body) is not null)
            .OrderBy(d => d.Rel, StringComparer.Ordinal)];
        if (open.Length == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.Append("# Wave mode\n\n");
        sb.Append("Generated by `plan-doc.cs wave-table --fix` while a wave is open, and deleted when it closes.\n");
        sb.Append("Don't edit it.\n");
        foreach (PlanDoc wave in open)
        {
            string w = Contract.WaveOf(wave.FileName)!;
            string n = w["wave-".Length..];
            sb.Append($"\n## Wave {n}\n\n");
            sb.Append($"- **Read first:** [waves-context.md](waves/waves-context.md). It says which tracks may start, and it wins\n");
            sb.Append("  where anything else disagrees.\n");
            sb.Append($"- **The wave document:** [{wave.Name}]({wave.Rel}): the gate, the contracts, who owns which\n");
            sb.Append("  files, the calendar.\n");
            sb.Append($"- **Messages go through the lead.** Tracks message `{w}-lead`, never each other.\n\n");
            sb.Append("| Start | In a VS Code extension tab | In a terminal |\n");
            sb.Append("| --- | --- | --- |\n");
            sb.Append($"| The lead | `/ai-plugins-and-skills:wave-workflow lead`, then rename the session `{w}-lead` | `claude --agent ai-plugins-and-skills:wave-lead --name {w}-lead` |\n");
            sb.Append($"| A track | `/ai-plugins-and-skills:wave-workflow track <track>`, then rename it `{w}-<track>` | `claude --agent ai-plugins-and-skills:wave-track --name {w}-<track>`, then `track <track>` |\n\n");
            sb.Append(WaveTable.Render(wave, docs, linkPrefix: string.Empty)).Append('\n');
        }

        return sb.ToString();
    }

    public static Finding? Check(string? current, IReadOnlyList<PlanDoc> docs)
    {
        string? expected = Render(docs);
        string? actual = current?.Replace("\r\n", "\n");
        return (expected, actual) switch
        {
            (null, null) => null,
            (null, _) => new Finding(FileName, "WAVE-README-STALE", "no wave is open; run plan-doc wave-table --fix to take the sign down"),
            (_, null) => new Finding(FileName, "WAVE-README-STALE", "a wave is open but the sign is missing; run plan-doc wave-table --fix"),
            _ when expected != actual => new Finding(FileName, "WAVE-README-STALE", "the sign is out of date or was edited; run plan-doc wave-table --fix"),
            _ => null,
        };
    }
}

// -----------------------------------------------------------------------------
// SPECS — system-spec. A system spec is the target system, never trimmed per release.
// Each feature section declares itself with a feature line:
//     **Feature:** rule-flows · **Status:** v1 interim
// and the status table between the markers is GENERATED from those lines, their
// interim blocks, and the plans whose delivers: names the feature. One parser for
// plans and specs is why this lives here and not in a second script.
// -----------------------------------------------------------------------------

static class Specs
{
    public const string Begin = "<!-- status-table:begin -->";

    public const string End = "<!-- status-table:end -->";

    public static readonly string[] DocumentStatuses = ["draft", "accepted", "retired"];

    public static readonly string[] RequiredFields = ["name", "area", "owner", "status"];

    // The statuses that are not a commitment to a named release.
    private static readonly string[] Unscheduled = ["Shipped", "Shipped interim", "Next up", "Later"];

    private static readonly Regex FeatureLine = new(@"^\*\*Feature:\*\*\s*(\S+)\s*·\s*\*\*Status:\*\*\s*(.*?)\s*$");

    private static readonly Regex Slug = new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$");

    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.*?)\s*#*\s*$");

    private static readonly Regex InterimField = new(@"^\s*[-*]\s+\*\*(Delivers|Change kind):\*\*\s*(.*?)\s*$");

    public static SpecDoc? Load(string path, string rel, string text)
    {
        (var fields, _, string body, bool found) = Fm.Parse(text);
        if (!found || fields.GetValueOrDefault("kind", "") != "spec")
        {
            return null;
        }

        int bodyStart = text.Replace("\r\n", "\n").Length - body.Length;
        int lineOffset = text.Replace("\r\n", "\n")[..bodyStart].Count(c => c == '\n');
        (var features, var bad) = ParseFeatures(body, lineOffset);
        return new SpecDoc(path, rel, fields, text, features, bad);
    }

    public static bool IsValidFeatureStatus(string status) =>
        Unscheduled.Contains(status, StringComparer.Ordinal)
        || Regex.IsMatch(status, @"^[A-Za-z0-9][A-Za-z0-9.\-]*( interim)?$");

    public static bool IsReleaseStatus(string status) =>
        IsValidFeatureStatus(status) && !Unscheduled.Contains(status, StringComparer.Ordinal);

    public static bool IsInterim(string status) => status.EndsWith(" interim", StringComparison.Ordinal);

    public static string RenderTable(SpecDoc spec, IReadOnlyList<PlanDoc> plans)
    {
        var lines = new List<string>
        {
            "| Feature | Section | Status | Delivered as | Change kind | Plan |",
            "| --- | --- | --- | --- | --- | --- |",
        };

        foreach (SpecFeature f in spec.Features)
        {
            string id = $"{spec.Name}#{f.Slug}";
            string delivered = f.HasInterim ? f.Delivers
                : f.Status is "Next up" or "Later" ? string.Empty
                : "Target";
            string planNames = string.Join(", ", plans
                .Where(p => p.Fields.TryGetValue(Contract.DeliversField, out string? d) && Fm.Items(d).Contains(id, StringComparer.Ordinal))
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .Select(n => $"`{n}`"));
            lines.Add($"| `{f.Slug}` | {Cell(f.Heading)} | {Cell(f.Status)} | {Cell(delivered)} | {Cell(f.HasInterim ? f.ChangeKind : string.Empty)} | {planNames} |");
        }

        return string.Join("\n", lines);
    }

    public static string? ApplyTable(string text, string table) => ApplyTable(text, table, Begin, End);

    // Shared with WaveTable: one way to find and replace a generated block between markers.
    public static string? ApplyTable(string text, string table, string beginMarker, string endMarker)
    {
        string eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string norm = text.Replace("\r\n", "\n");
        int begin = norm.IndexOf(beginMarker, StringComparison.Ordinal);
        int end = norm.IndexOf(endMarker, StringComparison.Ordinal);
        if (begin < 0 || end < begin)
        {
            return null;
        }

        string result = norm[..(begin + beginMarker.Length)] + "\n" + table + "\n" + norm[end..];
        return result.Replace("\n", eol);
    }

    public static IReadOnlyList<Finding> Check(IReadOnlyList<SpecDoc> specs, IReadOnlyList<PlanDoc> plans)
    {
        var findings = new List<Finding>();

        foreach (SpecDoc s in specs)
        {
            foreach (string field in RequiredFields.Where(f => s.Fields.GetValueOrDefault(f, "").Length == 0))
            {
                findings.Add(new Finding(s.Rel, "SPEC-MISSING-FIELD", $"a spec requires {field}:"));
            }

            string docStatus = s.Fields.GetValueOrDefault("status", "");
            if (docStatus.Length > 0 && !DocumentStatuses.Contains(docStatus))
            {
                findings.Add(new Finding(s.Rel, "SPEC-BAD-STATUS", $"status '{docStatus}' is not one of {string.Join(", ", DocumentStatuses)}"));
            }

            if (s.Name.Length > 0 && s.Name != s.Stem)
            {
                findings.Add(new Finding(s.Rel, "SPEC-NAME-MISMATCH", $"name '{s.Name}' is not the filename stem '{s.Stem}'"));
            }

            foreach (int line in s.BadFeatureLines)
            {
                findings.Add(new Finding(s.Rel, "SPEC-BAD-FEATURE", $"line {line}: a feature line is **Feature:** <kebab-slug> · **Status:** <status>"));
            }

            foreach (var dup in s.Features.GroupBy(f => f.Slug).Where(g => g.Count() > 1))
            {
                findings.Add(new Finding(s.Rel, "SPEC-DUPLICATE-FEATURE", $"feature '{dup.Key}' is declared {dup.Count()} times"));
            }

            foreach (SpecFeature f in s.Features)
            {
                if (!IsValidFeatureStatus(f.Status))
                {
                    findings.Add(new Finding(s.Rel, "SPEC-BAD-FEATURE-STATUS", $"line {f.Line}: '{f.Status}' is not Shipped, Shipped interim, <release>, <release> interim, Next up or Later"));
                }
                else if (IsInterim(f.Status) && !f.HasInterim)
                {
                    findings.Add(new Finding(s.Rel, "SPEC-NO-INTERIM", $"line {f.Line}: '{f.Slug}' is {f.Status} but its section has no interim block"));
                }
                else if (!IsInterim(f.Status) && f.HasInterim)
                {
                    findings.Add(new Finding(s.Rel, "SPEC-STALE-INTERIM", $"line {f.Line}: '{f.Slug}' is {f.Status} but still carries an interim block; delete it when the target ships"));
                }
            }

            string? current = CurrentTable(s.Text);
            if (current is null)
            {
                findings.Add(new Finding(s.Rel, "SPEC-NO-TABLE", $"no {Begin} ... {End} markers for the generated status table"));
            }
            else if (current != RenderTable(s, plans))
            {
                findings.Add(new Finding(s.Rel, "SPEC-TABLE-STALE", "the status table is out of date; run plan-doc spec-table --fix"));
            }
        }

        foreach (var dup in specs.Where(s => s.Name.Length > 0).GroupBy(s => s.Name).Where(g => g.Count() > 1))
        {
            foreach (SpecDoc s in dup)
            {
                findings.Add(new Finding(s.Rel, "SPEC-DUPLICATE-NAME", $"spec name '{dup.Key}' is used by {dup.Count()} specs"));
            }
        }

        foreach (PlanDoc p in plans)
        {
            if (!p.Fields.TryGetValue(Contract.DeliversField, out string? delivers))
            {
                continue;
            }

            foreach (string entry in Fm.Items(delivers).Where(Contract.IsDeliversEntry))
            {
                string[] parts = entry.Split('#');
                SpecDoc[] named = [.. specs.Where(s => s.Name == parts[0])];
                SpecFeature? feature = named.Length == 1 ? named[0].Features.FirstOrDefault(f => f.Slug == parts[1]) : null;
                if (feature is null)
                {
                    string why = named.Length == 0 ? $"no spec is named '{parts[0]}'" : named.Length > 1 ? $"'{parts[0]}' names {named.Length} specs" : $"spec '{parts[0]}' has no feature '{parts[1]}'";
                    findings.Add(new Finding(p.Rel, "DELIVERS-UNRESOLVED", $"delivers: '{entry}': {why}"));
                }
                else if (p.Status == "completed" && IsReleaseStatus(feature.Status))
                {
                    findings.Add(new Finding(p.Rel, "DELIVERS-STILL-RELEASE", $"this plan is completed but '{entry}' is still '{feature.Status}'; mark it Shipped or Shipped interim"));
                }
            }
        }

        return findings;
    }

    public static string? CurrentTable(string text) => CurrentTable(text, Begin, End);

    public static string? CurrentTable(string text, string beginMarker, string endMarker)
    {
        string norm = text.Replace("\r\n", "\n");
        int begin = norm.IndexOf(beginMarker, StringComparison.Ordinal);
        int end = norm.IndexOf(endMarker, StringComparison.Ordinal);
        return begin < 0 || end < begin ? null : norm[(begin + beginMarker.Length)..end].Trim('\n');
    }

    private static string Cell(string value) => value.Replace("|", @"\|", StringComparison.Ordinal);

    private static (List<SpecFeature> Features, List<int> Bad) ParseFeatures(string body, int lineOffset)
    {
        var features = new List<SpecFeature>();
        var bad = new List<int>();
        string[] lines = body.Split('\n');
        string heading = string.Empty;
        int level = 0;

        // Specs carry pseudocode, where "# comment" is not a heading and an example
        // feature line is not a feature. Fenced lines are invisible to both passes.
        bool[] fenced = new bool[lines.Length];
        bool open = false;
        for (int i = 0; i < lines.Length; i++)
        {
            bool fence = Regex.IsMatch(lines[i], @"^\s*(```|~~~)");
            fenced[i] = open || fence;
            open ^= fence;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            if (fenced[i])
            {
                continue;
            }

            Match h = Heading.Match(lines[i]);
            if (h.Success)
            {
                heading = h.Groups[2].Value;
                level = h.Groups[1].Value.Length;
                continue;
            }

            if (!lines[i].StartsWith("**Feature:**", StringComparison.Ordinal))
            {
                continue;
            }

            Match m = FeatureLine.Match(lines[i]);
            if (!m.Success || !Slug.IsMatch(m.Groups[1].Value))
            {
                bad.Add(lineOffset + i + 1);
                continue;
            }

            // The feature's section runs to the next heading at its own level or above.
            // An "Interim" heading inside it is the interim block.
            bool hasInterim = false;
            string delivers = string.Empty;
            string changeKind = string.Empty;
            bool inInterim = false;
            for (int j = i + 1; j < lines.Length; j++)
            {
                if (fenced[j])
                {
                    continue;
                }

                Match inner = Heading.Match(lines[j]);
                if (inner.Success)
                {
                    if (level > 0 && inner.Groups[1].Value.Length <= level)
                    {
                        break;
                    }

                    inInterim = inner.Groups[2].Value.StartsWith("Interim", StringComparison.Ordinal);
                    hasInterim |= inInterim;
                    continue;
                }

                Match field = InterimField.Match(lines[j]);
                if (inInterim && field.Success)
                {
                    string value = field.Groups[2].Value.TrimEnd('.');
                    if (field.Groups[1].Value == "Delivers")
                    {
                        delivers = value;
                    }
                    else
                    {
                        changeKind = value;
                    }
                }
            }

            features.Add(new SpecFeature(m.Groups[1].Value, heading, m.Groups[2].Value, hasInterim, delivers, changeKind, lineOffset + i + 1));
        }

        return (features, bad);
    }
}

static class Fm
{
    public static IReadOnlyList<string> Items(string value)
    {
        string inner = value.Trim();
        if (inner.StartsWith('[') && inner.EndsWith(']'))
        {
            inner = inner[1..^1];
        }

        return [.. inner.Split(',').Select(s => s.Trim().Trim('"', '\'')).Where(s => s.Length > 0)];
    }

    public static (Dictionary<string, string> Fields, string Raw, string Body, bool Found) Parse(string text)
    {
        string norm = text.Replace("\r\n", "\n");
        Match m = Regex.Match(norm, @"\A---\n(.*?)\n---\n", RegexOptions.Singleline);
        if (!m.Success)
        {
            return (new Dictionary<string, string>(StringComparer.Ordinal), string.Empty, norm, false);
        }

        string raw = m.Groups[1].Value;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        string? listKey = null;
        var listItems = new List<string>();
        foreach (string line in raw.Split('\n'))
        {
            // A block list ("delivers:" then "  - item" lines) is stored in its inline
            // spelling, so every reader sees one shape and Items() parses both.
            Match item = Regex.Match(line, @"^\s+-\s+(.+)$");
            if (listKey is not null && item.Success)
            {
                listItems.Add(item.Groups[1].Value.Trim());
                fields[listKey] = $"[{string.Join(", ", listItems)}]";
                continue;
            }

            listKey = null;
            Match f = Regex.Match(line, @"^([A-Za-z_][A-Za-z0-9_]*):\s*(.*)$");
            if (f.Success)
            {
                fields[f.Groups[1].Value] = f.Groups[2].Value.Trim();
                if (fields[f.Groups[1].Value].Length == 0)
                {
                    listKey = f.Groups[1].Value;
                    listItems.Clear();
                }
            }
        }

        return (fields, raw, norm[m.Length..], true);
    }

    public static string Rewrite(string raw, string newStatus, IReadOnlyList<(string Field, string Value)> extra)
    {
        string fm = Regex.Replace(raw, @"(?m)^status:.*$", $"status: {newStatus}");

        foreach ((string field, string value) in extra)
        {
            fm = Regex.IsMatch(fm, $@"(?m)^{Regex.Escape(field)}:")
                ? Regex.Replace(fm, $@"(?m)^{Regex.Escape(field)}:.*$", $"{field}: {value}")
                : fm.TrimEnd('\n') + $"\n{field}: {value}";
        }

        return fm.Trim('\n');
    }
}

static class Validator
{
    public static IReadOnlyList<Finding> Check(IReadOnlyList<PlanDoc> docs)
    {
        var findings = new List<Finding>();

        foreach (PlanDoc d in docs)
        {
            if (!d.HasFrontmatter)
            {
                findings.Add(new Finding(d.Rel, "NO-FRONTMATTER", "no frontmatter block; see plan-writing-syntax"));
                continue;
            }

            if (d.Kind.Length == 0)
            {
                findings.Add(new Finding(d.Rel, "NO-KIND", "no kind: field"));
                continue;
            }

            if (!Contract.Kinds.Contains(d.Kind))
            {
                findings.Add(new Finding(d.Rel, "BAD-KIND", $"kind '{d.Kind}' is not one of {string.Join(", ", Contract.Kinds)}"));
                continue;
            }

            if (d.Status.Length == 0)
            {
                findings.Add(new Finding(d.Rel, "NO-STATUS", "no status: field"));
                continue;
            }

            if (!Contract.StatusesFor(d.Kind).Contains(d.Status))
            {
                findings.Add(new Finding(
                    d.Rel,
                    "BAD-STATUS",
                    $"status '{d.Status}' is not valid for kind '{d.Kind}'; expected one of {string.Join(", ", Contract.StatusesFor(d.Kind))}"));
                continue;
            }

            string[] allowed = Contract.DirectoriesFor(d.Kind, d.Status);
            if (allowed.Length > 0 && !Contract.InAllowedDirectory(allowed, d.Dir))
            {
                findings.Add(new Finding(
                    d.Rel,
                    "DIR-MISMATCH",
                    $"{d.Kind}/{d.Status} belongs in {string.Join(" or ", allowed)}/ but sits in {d.Dir}/"));
            }

            string prefix = Contract.PrefixFor(d.Kind);
            string? foreign = Contract.ForeignPrefix(d.Kind, d.FileName);
            if (foreign is not null)
            {
                findings.Add(new Finding(d.Rel, "PREFIX-MISMATCH", $"the '{foreign}' prefix claims a kind this document is not ('{d.Kind}')"));
            }
            else if (Contract.PrefixRequired(d.Kind) && !d.FileName.StartsWith(prefix, StringComparison.Ordinal))
            {
                findings.Add(new Finding(d.Rel, "PREFIX-MISMATCH", $"kind '{d.Kind}' requires the '{prefix}' filename prefix"));
            }

            // wave: carries the prefix's fact inside the file, so it travels with a copy. A
            // wave-<n>_ plan must carry it and agree; without the prefix, wave: alone joins
            // the plan to its wave.
            string? prefixWave = d.Kind == "plan" ? Contract.WaveOf(d.FileName) : null;
            string fieldWave = d.Kind == "plan" ? d.Fields.GetValueOrDefault("wave", "") : "";
            if (fieldWave.Length > 0 && !Contract.IsWaveName(fieldWave))
            {
                findings.Add(new Finding(d.Rel, "BAD-WAVE", $"wave: '{fieldWave}' is not wave-<n>"));
            }
            else if (prefixWave is not null && fieldWave.Length == 0)
            {
                findings.Add(new Finding(d.Rel, "MISSING-FIELD", $"a '{prefixWave}_' plan requires wave: {prefixWave}"));
            }
            else if (prefixWave is not null && fieldWave != prefixWave)
            {
                findings.Add(new Finding(d.Rel, "WAVE-MISMATCH", $"wave: '{fieldWave}' disagrees with the '{prefixWave}_' prefix"));
            }

            // The wave has to exist: waves/wave-<n>/ holds its kind: wave document.
            if (d.Wave is string wave && !docs.Any(o => o.Kind == "wave" && o.Dir == "waves/" + wave))
            {
                findings.Add(new Finding(d.Rel, "UNKNOWN-WAVE", $"'{wave}' has no wave document in waves/{wave}/"));
            }

            if (d.Fields.TryGetValue("tier", out string? tier) && !Contract.Tiers.Contains(tier, StringComparer.Ordinal))
            {
                findings.Add(new Finding(d.Rel, "BAD-TIER", $"tier: '{tier}' is not one of {string.Join(", ", Contract.Tiers)} (spec-tier)"));
            }

            foreach (string field in Contract.NonEmptyFields.Where(f => d.Fields.TryGetValue(f, out string? v) && v.Length == 0))
            {
                findings.Add(new Finding(d.Rel, "EMPTY-FIELD", $"{field}: is present but empty"));
            }

            // Opt-in: a wave document carrying the markers has its plan table checked.
            if (WaveTable.HostsTable(d) && WaveTable.Current(d.Body) is string waveTable
                && waveTable != WaveTable.Render(d, docs))
            {
                findings.Add(new Finding(d.Rel, "WAVE-TABLE-STALE", "the plan table is out of date; run plan-doc wave-table --fix"));
            }

            foreach (string req in Contract.RequiredFields(d.Kind, d.Status))
            {
                string[] alternatives = req.Split('|');
                if (!alternatives.Any(a => d.Fields.ContainsKey(a) && d.Fields[a].Length > 0))
                {
                    findings.Add(new Finding(
                        d.Rel,
                        "MISSING-FIELD",
                        $"status '{d.Status}' requires {string.Join(" or ", alternatives.Select(a => a + ":"))}"));
                }
            }

            // A stub that turned out to be a bad idea ends the same way a plan does,
            // and is held to the same standard.
            if (Contract.OnTheBoard(d.Kind) && d.Status == "not-implemented"
                && d.Fields.TryGetValue("reason", out string? reason) && reason.Length > 0)
            {
                if (!Contract.ReasonFields.TryGetValue(reason, out string? namingField))
                {
                    findings.Add(new Finding(d.Rel, "BAD-REASON", $"reason '{reason}' is not one of {string.Join(", ", Contract.ReasonFields.Keys)}"));
                }
                else if (!d.Fields.ContainsKey(namingField))
                {
                    findings.Add(new Finding(d.Rel, "MISSING-FIELD", $"reason '{reason}' must name its target via {namingField}:"));
                }
            }

            // A track is a concurrency slot, not a plan-only affordance: a stub worked
            // directly occupies one exactly as a plan does.
            if (Contract.OnTheBoard(d.Kind) && d.Status == "in-progress" && !d.Fields.ContainsKey("track"))
            {
                findings.Add(new Finding(d.Rel, "NO-TRACK", $"an in-progress {d.Kind} must declare track:"));
            }

            if (d.Fields.TryGetValue(Contract.DeliversField, out string? delivers))
            {
                foreach (string entry in Fm.Items(delivers).Where(e => !Contract.IsDeliversEntry(e)))
                {
                    findings.Add(new Finding(d.Rel, "DELIVERS-SHAPE", $"delivers: '{entry}' is not <spec-name>#<feature-slug>; see the system-spec skill"));
                }
            }

            foreach (string field in Contract.DateFields)
            {
                if (d.Fields.TryGetValue(field, out string? when) && when.Length > 0
                    && !Contract.IsIsoDate(when))
                {
                    findings.Add(new Finding(d.Rel, "BAD-DATE", $"{field}: '{when}' is not ISO YYYY-MM-DD"));
                }
            }

            // References are names, never paths. This is the rule most likely to be
            // got wrong: a stored path is wrong the moment its target moves.
            foreach ((string field, string[] kinds) in Contract.ReferenceFields)
            {
                if (!d.Fields.TryGetValue(field, out string? target) || target.Length == 0)
                {
                    continue;
                }

                if (Names.LooksLikePath(target))
                {
                    findings.Add(new Finding(d.Rel, "PATH-NOT-NAME", $"{field}: '{target}' is a path; use the name (filename minus status prefix and .md)"));
                    continue;
                }

                if (target == d.Name)
                {
                    findings.Add(new Finding(d.Rel, "SELF-REFERENCE", $"{field}: names this document"));
                    continue;
                }

                PlanDoc[] matches = [.. docs.Where(o => o.Name == target && kinds.Contains(o.Kind))];
                if (matches.Length == 0)
                {
                    findings.Add(new Finding(d.Rel, "UNRESOLVED", $"{field}: '{target}' matches no {string.Join(" or ", kinds)}"));
                }
                else if (matches.Length > 1)
                {
                    findings.Add(new Finding(d.Rel, "AMBIGUOUS", $"{field}: '{target}' matches {matches.Length}: {string.Join(", ", matches.Select(m => m.Rel))}"));
                }
            }
        }

        // One document per track in in-progress/ -- a stub holds a slot like anything else.
        foreach (var g in docs.Where(d => Contract.OnTheBoard(d.Kind) && d.Status == "in-progress")
                     .GroupBy(d => d.Fields.GetValueOrDefault("track", ""))
                     .Where(g => g.Key.Length > 0 && g.Count() > 1))
        {
            foreach (PlanDoc d in g)
            {
                findings.Add(new Finding(d.Rel, "TRACK-COLLISION", $"track '{g.Key}' is claimed by {g.Count()} in-progress documents"));
            }
        }

        // Rule 2 of admission, and the one that actually protects the work: two
        // agents editing the same file is the failure the track field exists to
        // prevent.
        PlanDoc[] running = [.. docs.Where(d => Contract.OnTheBoard(d.Kind) && d.Status == "in-progress")];
        for (int i = 0; i < running.Length; i++)
        {
            for (int j = i + 1; j < running.Length; j++)
            {
                string[] overlap = [.. FileSet(running[i].Body).Intersect(FileSet(running[j].Body), StringComparer.OrdinalIgnoreCase)];
                if (overlap.Length > 0)
                {
                    findings.Add(new Finding(
                        running[i].Rel,
                        "FILESET-OVERLAP",
                        $"shares {overlap.Length} path(s) with {running[j].Rel}, e.g. {overlap[0]}; those plans are not independent"));
                }
            }
        }

        return findings;
    }

    public static IReadOnlyList<string> FileSet(string body)
    {
        Match section = Regex.Match(body, @"(?m)^##+\s*Files Expected to Change\s*$");
        if (!section.Success)
        {
            return [];
        }

        var paths = new List<string>();
        foreach (string line in body[(section.Index + section.Length)..].Split('\n'))
        {
            if (Regex.IsMatch(line, @"^##+\s"))
            {
                break;
            }

            Match item = Regex.Match(line, @"^\s*[-*]\s+`?([^\s`]+)`?");
            if (item.Success && item.Groups[1].Value.Contains('/'))
            {
                paths.Add(item.Groups[1].Value.Trim('`', ',', '.'));
            }
        }

        return paths;
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
                failures.Add($"{name}: expected '{expected}' got '{actual}'");
            }
        }

        static PlanDoc Doc(string rel, string fm, string body = "")
        {
            string text = $"---\n{fm}\n---\n{body}";
            (var f, _, string b, bool found) = Fm.Parse(text);
            return new PlanDoc(rel, rel, f, b, found);
        }

        string[] Rules(params PlanDoc[] docs) =>
            [.. Validator.Check(docs).Select(f => f.Rule).Distinct().OrderBy(r => r, StringComparer.Ordinal)];

        // --- frontmatter parsing ------------------------------------------------
        (var fields, string raw, string body, bool found) = Fm.Parse("---\nkind: plan\nstatus: queued\n---\n# Title\n");
        True_("frontmatter is found", found);
        Eq("kind parsed", fields["kind"], "plan");
        Eq("status parsed", fields["status"], "queued");
        Eq("body excludes the block", body, "# Title\n");
        Eq("raw block round-trips", raw, "kind: plan\nstatus: queued");

        (_, _, _, bool none) = Fm.Parse("# No frontmatter\n");
        True_("a document with no block is detected", !none);
        True_("a block must start at byte zero", !Fm.Parse("\n---\nkind: plan\n---\n").Found);
        Eq("CRLF input parses identically", Fm.Parse("---\r\nkind: plan\r\n---\r\nx\r\n").Fields["kind"], "plan");
        Eq("values are trimmed", Fm.Parse("---\nkind:   plan   \n---\n").Fields["kind"], "plan");
        True_("indented keys are not fields", !Fm.Parse("---\nkind: plan\n  nested: x\n---\n").Fields.ContainsKey("nested"));

        // --- names, not paths -----------------------------------------------------
        // A prefix names the document's KIND and is stable for its life, so a name is
        // the filename minus its KIND prefix. promoted_ and closed_ were retired with
        // the second lifecycle (stubs are baby plans now) -- nothing strips them any more.
        Eq("feature_ is stripped", Names.FromFileName("feature_foo_2026-09-14.md"), "foo_2026-09-14");
        Eq("stub_ is stripped", Names.FromFileName("stub_foo_2026-09-14.md"), "foo_2026-09-14");
        Eq("a plan with no prefix keeps its name", Names.FromFileName("2026-09-14-bug-thing.md"), "2026-09-14-bug-thing");

        // verify_ is a CATEGORY, not a kind prefix, and must survive.
        Eq("verify_ is not stripped", Names.FromFileName("verify_the-thing_2026-09-14.md"), "verify_the-thing_2026-09-14");
        Eq("follow-up_ is not stripped", Names.FromFileName("follow-up_x_2026-09-14.md"), "follow-up_x_2026-09-14");

        // The retired prefixes are no longer special: a file still carrying one keeps
        // it in its name, which is what makes the un-migrated document fail loudly.
        Eq("promoted_ is no longer stripped", Names.FromFileName("promoted_foo_2026-09-14.md"), "promoted_foo_2026-09-14");
        Eq("closed_ is no longer stripped", Names.FromFileName("closed_foo_2026-09-14.md"), "closed_foo_2026-09-14");

        True_("a slash is a path", Names.LooksLikePath("planning/completed/x.md"));
        True_("a backslash is a path", Names.LooksLikePath(@"planning\x.md"));
        True_("a bare .md is a path", Names.LooksLikePath("x.md"));
        True_("a plain name is not a path", !Names.LooksLikePath("seed-a-test-household_2026-08-31"));

        // The whole point of a kind prefix: a move never renames the file.
        Eq("a stub keeps its filename when promoted", Names.Rename("stub_x_2026-09-14.md", "stub", "completed"), "stub_x_2026-09-14.md");
        Eq("a stub keeps its filename when deferred", Names.Rename("stub_x_2026-09-14.md", "stub", "backlog"), "stub_x_2026-09-14.md");
        Eq("a plan keeps its filename across a move", Names.Rename("feature_x_2026-09-14.md", "plan", "completed"), "feature_x_2026-09-14.md");

        // --- the contract tables ---------------------------------------------------
        string[] boardVocab = ["backlog", "queued", "in-progress", "completed", "not-implemented", "blocked"];
        True_("plan statuses", Contract.StatusesFor("plan").SequenceEqual(boardVocab));
        True_("a stub shares the plan vocabulary", Contract.StatusesFor("stub").SequenceEqual(boardVocab));
        True_("reminder has dropped", Contract.StatusesFor("reminder").Contains("dropped"));
        True_("an unknown kind has no vocabulary", Contract.StatusesFor("nonsense").Length == 0);

        // The directory table is keyed by STATUS alone for plan and stub: folders are
        // status buckets, and a stub is a baby plan.
        foreach (string status in boardVocab)
        {
            True_(
                $"plan and stub agree on where {status} lives",
                Contract.DirectoriesFor("plan", status).SequenceEqual(Contract.DirectoriesFor("stub", status)));
        }

        True_("a queued plan lives at the root", Contract.DirectoriesFor("plan", "queued").SequenceEqual(["planning"]));
        True_("a deferred plan lives in backlog", Contract.DirectoriesFor("plan", "backlog").SequenceEqual(["backlog"]));
        True_("a promoted stub lives in completed", Contract.DirectoriesFor("stub", "completed").SequenceEqual(["completed"]));
        True_("a done reminder has two legal homes", Contract.DirectoriesFor("reminder", "done").Length == 2);
        Eq("a done review has one", Contract.DirectoriesFor("review", "done").Single(), "needs-review/completed");

        // The review pipeline moves an open review through three folders: plan-send-review
        // drops it in needs-review/, plan-intake-review moves it to in-progress/ and then
        // reviewed/. All three are the same status; only completed/ is done.
        const string OpenReview = "kind: review\nstatus: open\ndate: 2026-10-21";
        foreach (string folder in new[] { "needs-review", "needs-review/in-progress", "needs-review/reviewed" })
        {
            True_($"an open review may sit in {folder}/",
                !Rules(Doc($"{folder}/2026-10-21-a-plan.md", OpenReview)).Contains("DIR-MISMATCH"));
        }

        True_("a done review in reviewed/ is DIR-MISMATCH",
            Rules(Doc("needs-review/reviewed/2026-10-21-a-plan.md", "kind: review\nstatus: done\ndate: 2026-10-21")).Contains("DIR-MISMATCH"));
        True_("reviews: resolves to the plan under review",
            !Rules(
                Doc("needs-review/2026-10-21-a-plan.md", OpenReview + "\nreviews: a-plan_2026-10-21"),
                Doc("feature_a-plan_2026-10-21.md", "kind: plan\nstatus: queued\ndate: 2026-10-21")).Contains("UNRESOLVED"));
        True_("reviews: naming no plan is UNRESOLVED",
            Rules(Doc("needs-review/2026-10-21-a-plan.md", OpenReview + "\nreviews: no-such-plan_2026-10-21")).Contains("UNRESOLVED"));
        True_("reviews: given a path is PATH-NOT-NAME",
            Rules(Doc("needs-review/2026-10-21-a-plan.md", OpenReview + "\nreviews: planning/feature_a-plan_2026-10-21.md")).Contains("PATH-NOT-NAME"));
        Eq("feature_ marks a plan", Contract.PrefixFor("plan"), "feature_");
        Eq("stub_ marks a stub", Contract.PrefixFor("stub"), "stub_");
        Eq("a reminder has no kind prefix", Contract.PrefixFor("reminder"), "");

        // --- bug_ and wave-<n>_ are plan prefixes too ---------------------------------
        // bug_ is feature_'s sibling: a kind prefix, stripped, so a stub promoted to a bug
        // keeps its name. wave-<n>_ is not stripped: the wave is part of the name, so every
        // reference to the plan says which wave it belongs to.
        Eq("bug_ is stripped", Names.FromFileName("bug_foo_2026-09-14.md"), "foo_2026-09-14");
        Eq("wave-<n>_ stays in the name", Names.FromFileName("wave-1_read_2026-10-26.md"), "wave-1_read_2026-10-26");
        Eq("the wave is read from the prefix", Contract.WaveOf("wave-12_read_2026-10-26.md"), "wave-12");
        Eq("a hyphenless wave1_ is not a wave prefix", Contract.WaveOf("wave1_read_2026-10-26.md"), null);
        Eq("a plain plan names no wave", Contract.WaveOf("feature_read_2026-10-26.md"), null);
        Eq("a bug keeps its filename across a move", Names.Rename("bug_x_2026-09-14.md", "plan", "completed"), "bug_x_2026-09-14.md");

        const string QueuedPlan = "kind: plan\nstatus: queued\ndate: 2026-10-26";
        const string Wave1 = "kind: wave\nstatus: open\ndate: 2026-10-26";
        True_("a bug_ plan is not PREFIX-MISMATCH",
            !Rules(Doc("bug_x_2026-10-26.md", QueuedPlan)).Contains("PREFIX-MISMATCH"));
        True_("a bug_ stub is PREFIX-MISMATCH",
            Rules(Doc("backlog/bug_x_2026-10-26.md", "kind: stub\nstatus: backlog\ndate: 2026-10-26")).Contains("PREFIX-MISMATCH"));
        True_("a wave-<n>_ stub is PREFIX-MISMATCH",
            Rules(
                Doc("backlog/wave-1_x_2026-10-26.md", "kind: stub\nstatus: backlog\ndate: 2026-10-26"),
                Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1)).Contains("PREFIX-MISMATCH"));
        // A reminder needs no prefix, so only the foreign-prefix check can catch these.
        True_("a bug_ reminder is PREFIX-MISMATCH",
            Rules(Doc("reminders/bug_x_2026-10-26.md", "kind: reminder\nstatus: open\ndate: 2026-10-26")).Contains("PREFIX-MISMATCH"));
        True_("a wave-<n>_ reminder is PREFIX-MISMATCH",
            Rules(
                Doc("reminders/wave-1_x_2026-10-26.md", "kind: reminder\nstatus: open\ndate: 2026-10-26"),
                Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1)).Contains("PREFIX-MISMATCH"));
        const string WavePlan1 = QueuedPlan + "\nwave: wave-1";
        True_("a wave-<n>_ plan whose wave exists is clean",
            Rules(
                Doc("wave-1_read_2026-10-26.md", WavePlan1),
                Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1)).Length == 0);
        True_("a wave plan keeps its wave after it moves",
            Rules(
                Doc("completed/wave-1_read_2026-10-26.md", "kind: plan\nstatus: completed\ncompleted: 2026-10-30\nwave: wave-1"),
                Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1)).Length == 0);
        True_("a wave-<n>_ plan with no wave-<n>/ wave document is UNKNOWN-WAVE",
            Rules(Doc("wave-2_read_2026-10-26.md", QueuedPlan + "\nwave: wave-2"), Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1)).Contains("UNKNOWN-WAVE"));

        // --- wave:, tier:, author:, repo: -----------------------------------------------------
        // wave: carries the prefix's fact inside the file, so a copy taken out of the repo keeps
        // it. Checked redundancy: the two must agree. Without the prefix, wave: alone joins a
        // plan to its wave, so an older wave can adopt the convention without renaming.
        True_("a wave-<n>_ plan without wave: is MISSING-FIELD",
            Rules(Doc("wave-1_read_2026-10-26.md", QueuedPlan), Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1)).Contains("MISSING-FIELD"));
        True_("wave: disagreeing with the prefix is WAVE-MISMATCH",
            Rules(Doc("wave-1_read_2026-10-26.md", QueuedPlan + "\nwave: wave-2"), Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1)).Contains("WAVE-MISMATCH"));
        True_("wave: that is not wave-<n> is BAD-WAVE",
            Rules(Doc("feature_read_2026-10-26.md", QueuedPlan + "\nwave: wave1")).Contains("BAD-WAVE"));
        True_("wave: without the prefix joins an existing wave cleanly",
            Rules(Doc("feature_read_2026-10-26.md", WavePlan1), Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1)).Length == 0);
        True_("wave: without the prefix naming no wave is UNKNOWN-WAVE",
            Rules(Doc("feature_read_2026-10-26.md", QueuedPlan + "\nwave: wave-3")).Contains("UNKNOWN-WAVE"));
        True_("tier: low, medium or high is fine",
            !Rules(Doc("feature_t_2026-10-26.md", QueuedPlan + "\ntier: medium")).Contains("BAD-TIER"));
        True_("tier: outside spec-tier's words is BAD-TIER",
            Rules(Doc("feature_t_2026-10-26.md", QueuedPlan + "\ntier: High")).Contains("BAD-TIER"));
        True_("an empty author: is EMPTY-FIELD",
            Rules(Doc("feature_t_2026-10-26.md", QueuedPlan + "\nauthor:")).Contains("EMPTY-FIELD"));
        True_("an empty repo: is EMPTY-FIELD",
            Rules(Doc("feature_t_2026-10-26.md", QueuedPlan + "\nrepo:")).Contains("EMPTY-FIELD"));
        True_("author:, tier: and repo: together are clean",
            Rules(Doc("feature_t_2026-10-26.md", QueuedPlan + "\nauthor: Claude Code with Jane Doe\ntier: high\nrepo: Example.Orders.Api")).Length == 0);

        True_("a wave-<n>_ plan is resolvable by its full name",
            !Rules(
                Doc("wave-1_read_2026-10-26.md", WavePlan1),
                Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1),
                Doc("feature_umbrella_2026-10-26.md", QueuedPlan + "\npromoted_from: wave-1_read_2026-10-26")).Contains("UNRESOLVED"));
        True_("completed requires its date", Contract.RequiredFields("plan", "completed").SequenceEqual(["completed"]));
        True_("blocked requires a named blocker", Contract.RequiredFields("plan", "blocked").SequenceEqual(["blocked_on"]));
        True_("a stub is required to carry no more than a plan", Contract.RequiredFields("stub", "completed").SequenceEqual(["completed"]));
        True_("queued requires nothing", Contract.RequiredFields("plan", "queued").Length == 0);
        True_("backlog requires nothing", Contract.RequiredFields("plan", "backlog").Length == 0);

        // --- exemptions --------------------------------------------------------------
        True_("evidence is exempt", Contract.IsExempt("evidence/2026-09-10-draft.ps1"));
        True_("nested evidence is exempt", Contract.IsExempt("completed/evidence/x.md"));
        True_("README is exempt", Contract.IsExempt("backlog/README.md"));
        True_("a real plan is not exempt", !Contract.IsExempt("completed/feature_x_2026-09-14.md"));
        True_("a file merely named evidence.md is not exempt", !Contract.IsExempt("completed/evidence.md"));

        // --- validation ----------------------------------------------------------------
        Eq("a conforming plan yields nothing",
            Rules(Doc("in-progress/feature_a_2026-09-14.md", "kind: plan\nstatus: in-progress\ntrack: main")).Length, 0);
        True_("missing kind is caught",
            Rules(Doc("completed/x.md", "status: completed")).Contains("NO-KIND"));
        True_("an invented kind is caught",
            Rules(Doc("completed/x.md", "kind: epic\nstatus: completed")).Contains("BAD-KIND"));
        True_("an invented status is caught",
            Rules(Doc("completed/x.md", "kind: plan\nstatus: Incomplete")).Contains("BAD-STATUS"));

        // The retired stub vocabulary is now exactly as invalid as any other invention,
        // which is what makes an un-migrated repository report itself.
        True_("the retired stub status 'open' is caught",
            Rules(Doc("backlog/stub_x.md", "kind: stub\nstatus: open")).Contains("BAD-STATUS"));
        True_("the retired stub status 'promoted' is caught",
            Rules(Doc("backlog/stub_x.md", "kind: stub\nstatus: promoted\npromoted_to: p_2026-09-14")).Contains("BAD-STATUS"));
        True_("a status from the reminder vocabulary is caught on a stub",
            Rules(Doc("backlog/stub_x.md", "kind: stub\nstatus: dropped")).Contains("BAD-STATUS"));

        // The drift the whole arrangement exists to catch.
        True_("status and directory disagreeing is caught",
            Rules(Doc("planning/feature_x.md", "kind: plan\nstatus: completed\ncompleted: 2026-09-14")).Contains("DIR-MISMATCH"));
        True_("a completed plan in completed/ is fine",
            !Rules(Doc("completed/feature_x.md", "kind: plan\nstatus: completed\ncompleted: 2026-09-14")).Contains("DIR-MISMATCH"));
        True_("either reminder home is accepted",
            !Rules(Doc("reminders/verify_x.md", "kind: reminder\nstatus: done")).Contains("DIR-MISMATCH"));
        True_("the other reminder home is accepted too",
            !Rules(Doc("reminders/completed/verify_x.md", "kind: reminder\nstatus: done")).Contains("DIR-MISMATCH"));

        // A wave's documents may sit together in its own folder under waves/, one level
        // deep, so a finished wave can be filed as a unit beside its prompts.
        True_("a wave directly in waves/ is fine",
            !Rules(Doc("waves/wave-0_2026-10-19.md", "kind: wave\nstatus: open")).Contains("DIR-MISMATCH"));
        True_("a wave in its own folder under waves/ is fine",
            !Rules(Doc("waves/wave-0/wave-0_2026-10-19.md", "kind: wave\nstatus: done")).Contains("DIR-MISMATCH"));
        True_("a wave two folders deep is caught",
            Rules(Doc("waves/wave-0/old/wave-0_2026-10-19.md", "kind: wave\nstatus: open")).Contains("DIR-MISMATCH"));
        True_("a wave outside waves/ is caught",
            Rules(Doc("backlog/wave-0_2026-10-19.md", "kind: wave\nstatus: open")).Contains("DIR-MISMATCH"));
        True_("a wave folder holds waves, not plans",
            Rules(Doc("waves/wave-0/feature_x.md", "kind: plan\nstatus: queued")).Contains("DIR-MISMATCH"));

        // The context every wave's sessions read first. It outlives any one wave, so it
        // sits at the top of waves/ and nowhere else.
        True_("context is a kind", Contract.Kinds.Contains("context"));
        Eq("a context document is open or done", string.Join(",", Contract.StatusesFor("context")), "open,done");
        Eq("a current context document has no findings",
            Rules(Doc("waves/waves-context.md", "kind: context\nstatus: open")).Length, 0);
        True_("a context document inside one wave's folder is caught",
            Rules(Doc("waves/wave-0/waves-context.md", "kind: context\nstatus: open")).Contains("DIR-MISMATCH"));
        True_("a context document on the board is caught",
            Rules(Doc("backlog/waves-context.md", "kind: context\nstatus: open")).Contains("DIR-MISMATCH"));

        True_("completed without its date is caught",
            Rules(Doc("completed/feature_x.md", "kind: plan\nstatus: completed")).Contains("MISSING-FIELD"));
        True_("blocked without a named blocker is caught",
            Rules(Doc("blocked/feature_x.md", "kind: plan\nstatus: blocked")).Contains("MISSING-FIELD"));

        // A stub in backlog/ is the resting state and carries nothing beyond kind,
        // status and date -- capture has to stay cheap or ideas go uncaptured.
        True_("a deferred stub needs no extra field",
            Rules(Doc("backlog/stub_x_2026-09-14.md", "kind: stub\nstatus: backlog\ndate: 2026-09-14")).Length == 0);
        True_("a deferred plan is equally legal",
            Rules(Doc("backlog/feature_x_2026-09-14.md", "kind: plan\nstatus: backlog\ndate: 2026-09-14")).Length == 0);

        // Promotion IS a stub's completion; promoted_to: is judgement, not a gate.
        True_("a promoted stub without promoted_to is accepted",
            Rules(Doc("completed/stub_x_2026-09-14.md", "kind: stub\nstatus: completed\ncompleted: 2026-09-14")).Length == 0);
        True_("a promoted stub still needs its completed date",
            Rules(Doc("completed/stub_x.md", "kind: stub\nstatus: completed")).Contains("MISSING-FIELD"));
        True_("a promoted stub naming its plan resolves",
            !Rules(Doc("completed/stub_x_2026-09-14.md", "kind: stub\nstatus: completed\ncompleted: 2026-09-14\npromoted_to: p_2026-09-14"),
                   Doc("planning/feature_p_2026-09-14.md", "kind: plan\nstatus: queued")).Contains("UNRESOLVED"));

        // A stub MUST carry its prefix: it now shares every directory with plans, and
        // the prefix is the only thing telling the two apart at a glance.
        True_("a stub with no prefix is caught",
            Rules(Doc("backlog/an-idea_2026-09-14.md", "kind: stub\nstatus: backlog")).Contains("PREFIX-MISMATCH"));
        True_("a stub still carrying promoted_ is caught",
            Rules(Doc("completed/promoted_x_2026-09-14.md", "kind: stub\nstatus: completed\ncompleted: 2026-09-14")).Contains("PREFIX-MISMATCH"));
        True_("a stub still carrying feature_ is caught",
            Rules(Doc("backlog/feature_x_2026-09-14.md", "kind: stub\nstatus: backlog")).Contains("PREFIX-MISMATCH"));

        // A prefix belonging to ANOTHER kind is a lie, and lies are what this catches.
        True_("a plan carrying stub_ is caught",
            Rules(Doc("planning/stub_x_2026-09-14.md", "kind: plan\nstatus: queued")).Contains("PREFIX-MISMATCH"));
        True_("a reminder carrying stub_ is caught",
            Rules(Doc("reminders/stub_x_2026-09-14.md", "kind: reminder\nstatus: open")).Contains("PREFIX-MISMATCH"));

        // A MISSING prefix on a plan is a lost affordance, not a lie. Documents predating
        // the convention (phase_, or a plain descriptive name) are legible without it, and
        // renaming a completed document to satisfy a new rule is what the contract forbids.
        True_("a plan with no prefix is accepted",
            !Rules(Doc("completed/phase_skill-import_2026-04-29.md", "kind: plan\nstatus: completed\ncompleted: 2026-04-29")).Contains("PREFIX-MISMATCH"));
        True_("a plainly named plan is accepted",
            !Rules(Doc("completed/Codebase-Structure-Mapper.md", "kind: plan\nstatus: completed\ncompleted: 2026-04-29")).Contains("PREFIX-MISMATCH"));
        True_("a reminder's category prefix is still not a kind prefix",
            !Rules(Doc("reminders/verify_x_2026-09-14.md", "kind: reminder\nstatus: open")).Contains("PREFIX-MISMATCH"));

        True_("not-implemented without a reason is caught",
            Rules(Doc("not-implemented/feature_x.md", "kind: plan\nstatus: not-implemented")).Contains("MISSING-FIELD"));
        True_("an invented reason is caught",
            Rules(Doc("not-implemented/feature_x.md", "kind: plan\nstatus: not-implemented\nreason: boredom")).Contains("BAD-REASON"));
        True_("a reason naming nothing is caught",
            Rules(Doc("not-implemented/feature_x.md", "kind: plan\nstatus: not-implemented\nreason: abandoned")).Contains("MISSING-FIELD"));

        // The reason check used to be plan-only; a stub that turned out to be a bad
        // idea ends the same way and is held to the same standard.
        True_("a stub's invented reason is caught too",
            Rules(Doc("not-implemented/stub_x.md", "kind: stub\nstatus: not-implemented\nreason: boredom")).Contains("BAD-REASON"));
        True_("a stub folded into a plan resolves",
            !Rules(Doc("not-implemented/stub_x_2026-09-14.md", "kind: stub\nstatus: not-implemented\nreason: folded-in\nfolded_into: p_2026-09-14"),
                   Doc("completed/feature_p_2026-09-14.md", "kind: plan\nstatus: completed\ncompleted: 2026-09-14")).Contains("UNRESOLVED"));

        True_("an in-progress plan without a track is caught",
            Rules(Doc("in-progress/feature_x.md", "kind: plan\nstatus: in-progress")).Contains("NO-TRACK"));

        // A stub worked directly occupies a concurrency slot like anything else.
        True_("an in-progress stub without a track is caught",
            Rules(Doc("in-progress/stub_x.md", "kind: stub\nstatus: in-progress")).Contains("NO-TRACK"));
        True_("an in-progress stub with a track is fine",
            Rules(Doc("in-progress/stub_x.md", "kind: stub\nstatus: in-progress\ntrack: main")).Length == 0);

        // --- the decision fields -----------------------------------------------------
        // Optional, because status already carries the lifecycle. They record the one
        // fact it cannot: who made the call, and when.
        True_("a decided_on that is not a date is caught",
            Rules(Doc("completed/stub_x.md", "kind: stub\nstatus: completed\ncompleted: 2026-09-14\ndecided_on: last tuesday")).Contains("BAD-DATE"));
        True_("an ISO decided_on is accepted",
            !Rules(Doc("completed/stub_x.md", "kind: stub\nstatus: completed\ncompleted: 2026-09-14\ndecided_on: 2026-09-14\ndecided_by: Jane Doe")).Contains("BAD-DATE"));
        True_("both decision fields absent is fine",
            Rules(Doc("completed/stub_x.md", "kind: stub\nstatus: completed\ncompleted: 2026-09-14")).Length == 0);

        // --- references --------------------------------------------------------------
        const string PromotedStub = "kind: stub\nstatus: completed\ncompleted: 2026-09-14\npromoted_to: ";

        True_("a path where a name belongs is caught",
            Rules(Doc("completed/stub_x.md", PromotedStub + "planning/completed/feature_p_2026-09-14.md")).Contains("PATH-NOT-NAME"));
        True_("a reference to nothing is caught",
            Rules(Doc("completed/stub_x.md", PromotedStub + "does-not-exist")).Contains("UNRESOLVED"));
        True_("a resolving reference is accepted",
            !Rules(Doc("completed/stub_x_2026-09-14.md", PromotedStub + "p_2026-09-14"),
                   Doc("planning/feature_p_2026-09-14.md", "kind: plan\nstatus: queued")).Contains("UNRESOLVED"));
        True_("a reference resolving twice is caught",
            Rules(Doc("completed/stub_x.md", PromotedStub + "dup_2026-09-14"),
                  Doc("planning/feature_dup_2026-09-14.md", "kind: plan\nstatus: queued"),
                  Doc("completed/feature_dup_2026-09-14.md", "kind: plan\nstatus: completed\ncompleted: 2026-09-14")).Contains("AMBIGUOUS"));

        // A stub promoted the same day shares slug AND date with the plan it became.
        // Under kind prefixes the two filenames differ (stub_ vs feature_) but the
        // NAMES still collide, so kind is still what separates them.
        True_("kind disambiguates a same-name stub and plan",
            !Rules(Doc("completed/stub_x_2026-09-14.md", PromotedStub + "x_2026-09-14"),
                   Doc("planning/feature_x_2026-09-14.md", "kind: plan\nstatus: queued")).Contains("AMBIGUOUS"));
        True_("a self-reference is caught",
            Rules(Doc("completed/stub_x_2026-09-14.md", PromotedStub + "x_2026-09-14"),
                  Doc("backlog/stub_other.md", "kind: stub\nstatus: backlog")).Contains("SELF-REFERENCE"));

        // --- tracks and file sets --------------------------------------------------------
        True_("two plans on one track is caught",
            Rules(Doc("in-progress/feature_a.md", "kind: plan\nstatus: in-progress\ntrack: main"),
                  Doc("in-progress/feature_b.md", "kind: plan\nstatus: in-progress\ntrack: main")).Contains("TRACK-COLLISION"));
        True_("distinct tracks are fine",
            !Rules(Doc("in-progress/feature_a.md", "kind: plan\nstatus: in-progress\ntrack: main"),
                   Doc("in-progress/feature_b.md", "kind: plan\nstatus: in-progress\ntrack: engine")).Contains("TRACK-COLLISION"));

        // A track is a concurrency slot, not a plan-only affordance. A stub worked
        // directly collides with a plan on the same track exactly as another plan would.
        True_("a stub and a plan on one track is caught",
            Rules(Doc("in-progress/feature_a.md", "kind: plan\nstatus: in-progress\ntrack: main"),
                  Doc("in-progress/stub_b.md", "kind: stub\nstatus: in-progress\ntrack: main")).Contains("TRACK-COLLISION"));

        const string FilesA = "## Files Expected to Change\n- src/Shared.cs — reason\n- src/A.cs — reason\n";
        const string FilesB = "## Files Expected to Change\n- src/Shared.cs — reason\n- src/B.cs — reason\n";
        const string FilesC = "## Files Expected to Change\n- src/C.cs — reason\n";

        True_("the file set is extracted",
            Validator.FileSet(FilesA).SequenceEqual(["src/Shared.cs", "src/A.cs"]));
        True_("extraction stops at the next heading",
            Validator.FileSet("## Files Expected to Change\n- src/A.cs — r\n\n## Next\n- src/B.cs — r\n")
                .SequenceEqual(["src/A.cs"]));
        True_("a missing section yields nothing", Validator.FileSet("# Plan\n").Count == 0);
        True_("prose bullets are not paths", Validator.FileSet("## Files Expected to Change\n- nothing yet\n").Count == 0);

        True_("overlapping file sets are caught",
            Rules(Doc("in-progress/feature_a.md", "kind: plan\nstatus: in-progress\ntrack: main", FilesA),
                  Doc("in-progress/feature_b.md", "kind: plan\nstatus: in-progress\ntrack: engine", FilesB)).Contains("FILESET-OVERLAP"));
        True_("a stub's file set collides like a plan's",
            Rules(Doc("in-progress/feature_a.md", "kind: plan\nstatus: in-progress\ntrack: main", FilesA),
                  Doc("in-progress/stub_b.md", "kind: stub\nstatus: in-progress\ntrack: engine", FilesB)).Contains("FILESET-OVERLAP"));
        True_("disjoint file sets are fine",
            !Rules(Doc("in-progress/feature_a.md", "kind: plan\nstatus: in-progress\ntrack: main", FilesA),
                   Doc("in-progress/feature_c.md", "kind: plan\nstatus: in-progress\ntrack: engine", FilesC)).Contains("FILESET-OVERLAP"));

        // --- close and validate must not disagree ----------------------------------------
        // They did. `close` hardcoded a switch over completed/blocked/not-implemented and
        // simply had no case for a promoted stub, so it produced a document that its own
        // validate verb then rejected for a missing promoted_to:. Sharing the parser was
        // never enough -- the required-field GATE has to read the same table too.
        foreach (string kind in Contract.Kinds)
        {
            foreach (string status in Contract.StatusesFor(kind))
            {
                foreach (string req in Contract.RequiredFields(kind, status))
                {
                    True_(
                        $"close can satisfy {kind}/{status} -> {req}",
                        req.Split('|').Any(CloseFields.CanSupply));
                }
            }
        }

        True_("promoted_to is a field close knows how to supply", CloseFields.CanSupply("promoted_to"));
        True_("decided_by is a field close knows how to supply", CloseFields.CanSupply("decided_by"));
        True_("decided_on is a field close knows how to supply", CloseFields.CanSupply("decided_on"));
        True_("the retired closed_by is gone", !CloseFields.CanSupply("closed_by"));
        True_("the retired closed_reason is gone", !CloseFields.CanSupply("closed_reason"));
        True_("an invented field is not suppliable", !CloseFields.CanSupply("nonsense"));

        // close's date gate and validate's BAD-DATE rule call the SAME function, so the
        // two verbs cannot drift into disagreeing about what a date is.
        True_("an ISO date passes", Contract.IsIsoDate("2026-09-16"));
        True_("prose is not a date", !Contract.IsIsoDate("last tuesday"));
        True_("a partial date is not a date", !Contract.IsIsoDate("2026-09"));
        True_("a trailing suffix is not a date", !Contract.IsIsoDate("2026-09-16 (approx)"));
        True_("every date field close can supply is one validate shape-checks",
            Contract.DateFields.All(CloseFields.CanSupply));

        // --- close's frontmatter rewrite -------------------------------------------------
        Eq("status is replaced in place",
            Fm.Rewrite("kind: plan\nstatus: in-progress\ntrack: main", "completed", [("completed", "2026-09-14")]),
            "kind: plan\nstatus: completed\ntrack: main\ncompleted: 2026-09-14");
        Eq("an existing field is updated, not duplicated",
            Fm.Rewrite("kind: plan\nstatus: blocked\nblocked_on: old", "blocked", [("blocked_on", "new")]),
            "kind: plan\nstatus: blocked\nblocked_on: new");
        Eq("other fields survive untouched",
            Fm.Rewrite("kind: plan\nstatus: queued\ntrack: main\ndate: 2026-09-01", "in-progress", []),
            "kind: plan\nstatus: in-progress\ntrack: main\ndate: 2026-09-01");
        Eq("two extra fields are both added",
            Fm.Rewrite("kind: plan\nstatus: in-progress", "not-implemented", [("reason", "abandoned"), ("learned", "it was slower")]),
            "kind: plan\nstatus: not-implemented\nreason: abandoned\nlearned: it was slower");
        True_("rewriting does not touch the body",
            Fm.Parse("---\nkind: plan\nstatus: queued\n---\n# Body\n\ntext\n").Body == "# Body\n\ntext\n");

        // --- delivers: (system-spec) ------------------------------------------------------
        // A plan cites the spec features it delivers by spec name and feature slug.
        // The field is a list, and the parser has to read both YAML spellings of one.
        Eq("an inline list is read",
            string.Join("|", Fm.Items(Fm.Parse("---\nkind: plan\ndelivers: [a-spec#one, a-spec#two]\n---\n").Fields["delivers"])),
            "a-spec#one|a-spec#two");
        Eq("a block list is read",
            string.Join("|", Fm.Items(Fm.Parse("---\nkind: plan\ndelivers:\n  - a-spec#one\n  - a-spec#two\nstatus: queued\n---\n").Fields["delivers"])),
            "a-spec#one|a-spec#two");
        Eq("a block list does not swallow the next field",
            Fm.Parse("---\nkind: plan\ndelivers:\n  - a-spec#one\nstatus: queued\n---\n").Fields["status"], "queued");
        Eq("a single value is a one-item list", string.Join("|", Fm.Items("a-spec#one")), "a-spec#one");
        Eq("an empty value is an empty list", Fm.Items("").Count, 0);

        string Plan(string status, string delivers) =>
            $"kind: plan\nstatus: {status}\ntrack: main\ndate: 2026-11-02{(status == "completed" ? "\ncompleted: 2026-11-09" : "")}\ndelivers: {delivers}";
        True_("a well-shaped delivers: entry passes",
            !Rules(Doc("in-progress/feature_flows_2026-11-02.md", Plan("in-progress", "[pricing-engine-spec#rule-flows]"))).Contains("DELIVERS-SHAPE"));
        True_("a path in delivers: is DELIVERS-SHAPE",
            Rules(Doc("in-progress/feature_flows_2026-11-02.md", Plan("in-progress", "[docs/specs/pricing.md#rule-flows]"))).Contains("DELIVERS-SHAPE"));
        True_("a heading anchor in place of a slug is DELIVERS-SHAPE",
            Rules(Doc("in-progress/feature_flows_2026-11-02.md", Plan("in-progress", "[pricing-engine-spec#Rule Flows]"))).Contains("DELIVERS-SHAPE"));
        True_("an entry without a feature is DELIVERS-SHAPE",
            Rules(Doc("in-progress/feature_flows_2026-11-02.md", Plan("in-progress", "[pricing-engine-spec]"))).Contains("DELIVERS-SHAPE"));

        // --- specs (system-spec) -----------------------------------------------------------
        const string SpecFm = "---\nkind: spec\nname: pricing-engine-spec\narea: pricing\nowner: Jane Doe\nstatus: accepted\n---\n";
        const string SpecBody =
            "\n# Pricing engine\n\n" +
            "<!-- status-table:begin -->\n<!-- status-table:end -->\n\n" +
            "## Rule flows\n**Feature:** rule-flows · **Status:** v1 interim\n\nTarget text.\n\n" +
            "#### Interim (v1)\n\n" +
            "- **Delivers:** run, if and end steps; no forEach.\n" +
            "- **Omits:** loops.\n" +
            "- **Change kind:** Contract subset.\n" +
            "- **Retire when:** the flow designer ships.\n\n" +
            "## Scenario tests\n**Feature:** scenario-tests · **Status:** Next up\n\n" +
            "## Expression language\n**Feature:** expressions · **Status:** Shipped\n\n" +
            "## Glossary\n\nNot a feature.\n";
        const string SpecPath = "docs/specs/pricing/pricing-engine-spec.md";

        SpecDoc? Spec(string fm, string body, string rel = SpecPath) => Specs.Load(rel, rel, fm + body);

        string[] SpecRules(SpecDoc?[] specs, params PlanDoc[] plans) =>
            [.. Specs.Check([.. specs.OfType<SpecDoc>()], plans).Select(f => f.Rule).Distinct().OrderBy(r => r, StringComparer.Ordinal)];

        PlanDoc flows = Doc("in-progress/feature_pricing-rule-flows_2026-11-02.md", Plan("in-progress", "[pricing-engine-spec#rule-flows]"));
        PlanDoc expr = Doc("completed/feature_pricing-expressions_2026-10-01.md", Plan("completed", "[pricing-engine-spec#expressions]"));

        SpecDoc? spec = Spec(SpecFm, SpecBody);
        True_("a kind: spec document loads", spec is not null);
        True_("a document that is not kind: spec does not load", Spec("---\nkind: plan\nstatus: queued\n---\n", SpecBody) is null);
        Eq("only sections with a feature line are features", spec?.Features.Count, 3);
        Eq("feature slug", spec?.Features.ElementAtOrDefault(0)?.Slug, "rule-flows");
        Eq("feature heading", spec?.Features.ElementAtOrDefault(0)?.Heading, "Rule flows");
        Eq("feature status", spec?.Features.ElementAtOrDefault(0)?.Status, "v1 interim");
        True_("an interim block is found", spec?.Features.ElementAtOrDefault(0)?.HasInterim == true);
        Eq("Delivers is read from the interim block", spec?.Features.ElementAtOrDefault(0)?.Delivers, "run, if and end steps; no forEach");
        Eq("Change kind is read from the interim block", spec?.Features.ElementAtOrDefault(0)?.ChangeKind, "Contract subset");
        True_("a feature with no interim block has none", spec?.Features.ElementAtOrDefault(1)?.HasInterim == false);

        True_("a release is a release status", Specs.IsReleaseStatus("v1"));
        True_("a release interim is a release status", Specs.IsReleaseStatus("v1 interim"));
        True_("Shipped interim is not a release status", !Specs.IsReleaseStatus("Shipped interim"));
        True_("Next up is not a release status", !Specs.IsReleaseStatus("Next up"));
        True_("prose is not a feature status", !Specs.IsValidFeatureStatus("soon, maybe"));

        string expectedTable =
            "| Feature | Section | Status | Delivered as | Change kind | Plan |\n" +
            "| --- | --- | --- | --- | --- | --- |\n" +
            "| `rule-flows` | Rule flows | v1 interim | run, if and end steps; no forEach | Contract subset | `pricing-rule-flows_2026-11-02` |\n" +
            "| `scenario-tests` | Scenario tests | Next up |  |  |  |\n" +
            "| `expressions` | Expression language | Shipped | Target |  | `pricing-expressions_2026-10-01` |";
        Eq("the table is rendered from the sections and the plans", spec is null ? null : Specs.RenderTable(spec, [flows, expr]), expectedTable);

        True_("an empty table is SPEC-TABLE-STALE", SpecRules([spec], flows, expr).Contains("SPEC-TABLE-STALE"));
        string? fixedText = spec is null ? null : Specs.ApplyTable(spec.Text, Specs.RenderTable(spec, [flows, expr]));
        SpecDoc? fixedSpec = fixedText is null ? null : Specs.Load(SpecPath, SpecPath, fixedText);
        Eq("a fixed spec has no findings", fixedSpec is null ? "not loaded" : string.Join(",", SpecRules([fixedSpec], flows, expr)), "");
        True_("a plan added later makes the table stale again",
            fixedSpec is not null && SpecRules([fixedSpec], flows, expr, Doc("feature_pricing-scenarios_2026-12-01.md", Plan("queued", "[pricing-engine-spec#scenario-tests]"))).Contains("SPEC-TABLE-STALE"));
        True_("--fix keeps CRLF line endings",
            Specs.ApplyTable("a\r\n<!-- status-table:begin -->\r\n<!-- status-table:end -->\r\nb\r\n", "| x |") == "a\r\n<!-- status-table:begin -->\r\n| x |\r\n<!-- status-table:end -->\r\nb\r\n");
        True_("no markers means no table to apply", Specs.ApplyTable("no markers\n", "| x |") is null);
        True_("a spec with no markers is SPEC-NO-TABLE",
            SpecRules([Spec(SpecFm, SpecBody.Replace("<!-- status-table:begin -->\n<!-- status-table:end -->\n", string.Empty))]).Contains("SPEC-NO-TABLE"));
        True_("a pipe in a cell is escaped",
            Specs.RenderTable(Spec(SpecFm, "## A\n**Feature:** a · **Status:** v1 interim\n#### Interim (v1)\n- **Delivers:** a | b\n")!, []).Contains(@"a \| b"));

        SpecDoc? bounded = Spec(SpecFm, "## A\n**Feature:** a · **Status:** Next up\n\n### Detail\n\n## B\n**Feature:** b · **Status:** v1 interim\n\n#### Interim (v1)\n- **Delivers:** b only\n");
        True_("a section ends at the next heading of its own level", bounded?.Features.ElementAtOrDefault(0)?.HasInterim == false);
        True_("a deeper heading stays inside the section", bounded?.Features.ElementAtOrDefault(1)?.Delivers == "b only");

        SpecDoc? fenced = Spec(SpecFm, "## Flows\n**Feature:** flows · **Status:** v1 interim\n\n```python\n# a comment, not a heading\nrun()\n```\n\n#### Interim (v1)\n- **Delivers:** run only\n\n```\n**Feature:** not-a-feature · **Status:** v1\n```\n");
        True_("a # line inside a code fence is not a heading", fenced?.Features.ElementAtOrDefault(0)?.Heading == "Flows" && fenced.Features[0].HasInterim);
        Eq("a feature line inside a code fence is not a feature", fenced?.Features.Count, 1);

        True_("an interim status with no interim block is SPEC-NO-INTERIM",
            SpecRules([Spec(SpecFm, SpecBody.Replace("Next up", "v2 interim"))]).Contains("SPEC-NO-INTERIM"));
        True_("an interim block on a non-interim feature is SPEC-STALE-INTERIM",
            SpecRules([Spec(SpecFm, SpecBody.Replace("**Status:** v1 interim", "**Status:** Shipped"))]).Contains("SPEC-STALE-INTERIM"));
        True_("an unknown feature status is SPEC-BAD-FEATURE-STATUS",
            SpecRules([Spec(SpecFm, SpecBody.Replace("Next up", "soon, maybe"))]).Contains("SPEC-BAD-FEATURE-STATUS"));
        True_("a feature line with a bad slug is SPEC-BAD-FEATURE",
            SpecRules([Spec(SpecFm, SpecBody.Replace("**Feature:** scenario-tests", "**Feature:** Scenario Tests"))]).Contains("SPEC-BAD-FEATURE"));
        True_("a repeated slug is SPEC-DUPLICATE-FEATURE",
            SpecRules([Spec(SpecFm, SpecBody.Replace("**Feature:** scenario-tests", "**Feature:** rule-flows"))]).Contains("SPEC-DUPLICATE-FEATURE"));
        True_("a name that is not the filename stem is SPEC-NAME-MISMATCH",
            SpecRules([Spec(SpecFm.Replace("name: pricing-engine-spec", "name: pricing"), SpecBody)]).Contains("SPEC-NAME-MISMATCH"));
        True_("a spec without an owner is SPEC-MISSING-FIELD",
            SpecRules([Spec(SpecFm.Replace("owner: Jane Doe\n", string.Empty), SpecBody)]).Contains("SPEC-MISSING-FIELD"));
        True_("a document status outside draft|accepted|retired is SPEC-BAD-STATUS",
            SpecRules([Spec(SpecFm.Replace("status: accepted", "status: released"), SpecBody)]).Contains("SPEC-BAD-STATUS"));
        True_("two specs with one name are SPEC-DUPLICATE-NAME",
            SpecRules([spec, Spec(SpecFm, SpecBody, "other/pricing-engine-spec.md")]).Contains("SPEC-DUPLICATE-NAME"));

        True_("a delivers: entry naming no spec is DELIVERS-UNRESOLVED",
            fixedSpec is not null && SpecRules([fixedSpec], Doc("feature_x_2026-11-02.md", Plan("queued", "[no-such-spec#rule-flows]"))).Contains("DELIVERS-UNRESOLVED"));
        True_("a delivers: entry naming no feature is DELIVERS-UNRESOLVED",
            fixedSpec is not null && SpecRules([fixedSpec], Doc("feature_x_2026-11-02.md", Plan("queued", "[pricing-engine-spec#no-such-feature]"))).Contains("DELIVERS-UNRESOLVED"));
        True_("a completed plan whose feature still has a release status is DELIVERS-STILL-RELEASE",
            fixedSpec is not null && SpecRules([fixedSpec], Doc("completed/feature_pricing-rule-flows_2026-11-02.md", Plan("completed", "[pricing-engine-spec#rule-flows]"))).Contains("DELIVERS-STILL-RELEASE"));
        True_("a completed plan whose feature shipped is fine",
            fixedSpec is not null && !SpecRules([fixedSpec], flows, expr).Contains("DELIVERS-STILL-RELEASE"));

        // --- wave-table -------------------------------------------------------------------
        // The wave document's list of its plans is generated from the wave-<n>_ prefix, so
        // it says where each plan sits now instead of where it sat when someone typed it.
        const string WaveBody = "\n# Wave 1\n\n<!-- wave-table:begin -->\n<!-- wave-table:end -->\n";
        PlanDoc waveDoc = Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1, WaveBody);
        PlanDoc leadPlan = Doc("wave-1_lead_2026-10-26.md", "kind: plan\nstatus: queued\ntrack: lead\nwave: wave-1\ntier: high\nrepo: Example.Orders");
        PlanDoc readPlan = Doc("in-progress/wave-1_read_2026-10-26.md", "kind: plan\nstatus: in-progress\ntrack: read\nwave: wave-1\ntier: high\nrepo: Example.Orders.Api");
        PlanDoc joinedPlan = Doc("feature_legacy_2026-10-20.md", "kind: plan\nstatus: queued\ntrack: legacy\nwave: wave-1\ntier: medium");
        PlanDoc otherPlan = Doc("feature_x_2026-10-26.md", "kind: plan\nstatus: queued\ntrack: x");
        PlanDoc wave2Plan = Doc("wave-2_read_2026-11-02.md", "kind: plan\nstatus: queued\ntrack: read\nwave: wave-2");
        PlanDoc[] board = [waveDoc, leadPlan, readPlan, joinedPlan, otherPlan, wave2Plan];
        Eq("the wave table lists the wave's plans, by prefix or by wave:, where they sit now",
            WaveTable.Render(waveDoc, board),
            "| Plan | Track | Tier | Repo | Status |\n| --- | --- | --- | --- | --- |\n" +
            "| [`legacy_2026-10-20`](../../feature_legacy_2026-10-20.md) | legacy | medium |  | queued |\n" +
            "| [`wave-1_lead_2026-10-26`](../../wave-1_lead_2026-10-26.md) | lead | high | Example.Orders | queued |\n" +
            "| [`wave-1_read_2026-10-26`](../../in-progress/wave-1_read_2026-10-26.md) | read | high | Example.Orders.Api | in-progress |");
        True_("the wave document hosts the table",
            WaveTable.HostsTable(waveDoc));
        True_("a supporting wave document does not",
            !WaveTable.HostsTable(Doc("waves/wave-1/people-only-work.md", Wave1)));
        True_("an empty wave table is WAVE-TABLE-STALE",
            Rules(board).Contains("WAVE-TABLE-STALE"));
        PlanDoc fixedWave = Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1, WaveTable.Apply(WaveBody, WaveTable.Render(waveDoc, board))!);
        True_("a regenerated wave table is current",
            !Rules(fixedWave, leadPlan, readPlan, joinedPlan, otherPlan, wave2Plan).Contains("WAVE-TABLE-STALE"));
        True_("a plan that moves makes the wave table stale again",
            Rules(fixedWave, leadPlan, joinedPlan, otherPlan, wave2Plan,
                Doc("completed/wave-1_read_2026-10-26.md", "kind: plan\nstatus: completed\ncompleted: 2026-10-30\ntrack: read\nwave: wave-1\ntier: high\nrepo: Example.Orders.Api")).Contains("WAVE-TABLE-STALE"));
        True_("a wave document without the markers is not checked",
            !Rules(Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1), leadPlan).Contains("WAVE-TABLE-STALE"));

        // --- the wave sign ------------------------------------------------------------------
        // planning/WAVE-README.md exists exactly while a wave is open: its presence is the
        // signal. It is generated, so it is never stale, and it is signage, not a document.
        True_("WAVE-README.md at the board's root is signage",
            Contract.IsExempt("WAVE-README.md"));
        string? sign = WaveSign.Render(board);
        True_("an open wave puts up the sign",
            sign is not null && sign.Contains("## Wave 1", StringComparison.Ordinal));
        True_("the sign links each plan from the board's root, where it sits now",
            sign is not null && sign.Contains("| [`wave-1_read_2026-10-26`](in-progress/wave-1_read_2026-10-26.md) | read | high | Example.Orders.Api | in-progress |", StringComparison.Ordinal));
        True_("the sign names the lead's session",
            sign is not null && sign.Contains("wave-1-lead", StringComparison.Ordinal));
        True_("the sign leaves out other waves' plans",
            sign is not null && !sign.Contains("wave-2_read", StringComparison.Ordinal));
        PlanDoc doneWave = Doc("waves/wave-1/wave-1_2026-10-26.md", "kind: wave\nstatus: done\ndate: 2026-10-26", WaveBody);
        True_("a done wave takes the sign down",
            WaveSign.Render([doneWave, leadPlan]) is null);
        True_("a wave without the table markers puts up no sign",
            WaveSign.Render([Doc("waves/wave-1/wave-1_2026-10-26.md", Wave1), leadPlan]) is null);
        True_("a missing sign during a wave is WAVE-README-STALE",
            WaveSign.Check(null, board)?.Rule == "WAVE-README-STALE");
        True_("a current sign is fine, whatever its line endings",
            sign is not null && WaveSign.Check(sign.Replace("\n", "\r\n"), board) is null);
        True_("an edited sign is WAVE-README-STALE",
            WaveSign.Check(sign + "\nhand edit\n", board)?.Rule == "WAVE-README-STALE");
        True_("a sign left up after the wave is WAVE-README-STALE",
            WaveSign.Check("# Wave mode\n", [doneWave, leadPlan])?.Rule == "WAVE-README-STALE");
        True_("no wave and no sign is fine",
            WaveSign.Check(null, [doneWave, leadPlan]) is null);

        foreach (string f in failures)
        {
            Console.Error.WriteLine($"FAIL  {f}");
        }

        Console.WriteLine($"selftest: {count - failures.Count}/{count} passed");
        return failures.Count == 0 ? 0 : 1;
    }
}
