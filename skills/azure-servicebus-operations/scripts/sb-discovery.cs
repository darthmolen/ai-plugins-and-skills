#:package Azure.Messaging.ServiceBus@7.18.4
#:package Azure.Identity@1.13.2

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;

#pragma warning disable IL2026
#pragma warning disable IL3050

// Enumerate Service Bus topics, queues, and subscriptions in a namespace.
// Output: hierarchical text by default, JSON with --json, optional --out=FILE for persistence.
// Optional positional entity args narrow the scope (auto-detected as topic / queue / topic-sub).

if (args.Length < 1 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("Usage: dotnet run sb-discovery.cs <namespace> [<entity> ...] [--out=FILE] [--json] [--with-counts]");
    Console.Error.WriteLine("  <entity>        Optional. Zero or more focus filters:");
    Console.Error.WriteLine("                    queueName              -> just that queue");
    Console.Error.WriteLine("                    topicName              -> that topic + its subscriptions");
    Console.Error.WriteLine("                    topicName/subName      -> just that subscription");
    Console.Error.WriteLine("                  Type is auto-detected. Omit for whole-namespace shotgun.");
    Console.Error.WriteLine("  --out=FILE      Write the JSON snapshot to FILE (in addition to stdout).");
    Console.Error.WriteLine("  --json          Stdout as JSON instead of hierarchical text.");
    Console.Error.WriteLine("  --with-counts   Include ActiveMessageCount + DeadLetterMessageCount on each entity (slower).");
    return 1;
}

string ns = args[0];
string? outFile = null;
bool json = false;
bool withCounts = false;
var entityFilters = new List<string>();

foreach (var arg in args.Skip(1))
{
    if (arg.StartsWith("--out=", StringComparison.Ordinal)) outFile = arg["--out=".Length..];
    else if (arg == "--json") json = true;
    else if (arg == "--with-counts") withCounts = true;
    else if (arg.StartsWith("--", StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"unknown flag: {arg}");
        return 1;
    }
    else entityFilters.Add(arg);
}

string fqdn = ns.Contains('.') ? ns : $"{ns}.servicebus.windows.net";
var admin = new ServiceBusAdministrationClient(fqdn, new DefaultAzureCredential());

var snapshot = new Snapshot
{
    Namespace = fqdn,
    DiscoveredAt = DateTimeOffset.UtcNow,
    WithCounts = withCounts,
};

if (entityFilters.Count == 0)
{
    // Shotgun: enumerate the whole namespace.
    await EnumerateAllAsync(admin, snapshot, withCounts);
}
else
{
    // Narrow: resolve each filter and add to the snapshot.
    await EnumerateFiltersAsync(admin, snapshot, entityFilters, withCounts);
}

// Sort for stable output.
snapshot.Topics.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
foreach (var t in snapshot.Topics)
{
    t.Subscriptions.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
}
snapshot.Queues.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
};
string jsonText = JsonSerializer.Serialize(snapshot, jsonOptions);

if (outFile is not null)
{
    await File.WriteAllTextAsync(outFile, jsonText);
    Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:o}] wrote {jsonText.Length:N0} bytes to {Path.GetFullPath(outFile)}");
}

if (json)
{
    Console.WriteLine(jsonText);
}
else
{
    PrintHierarchicalText(snapshot);
}

return 0;

static async Task EnumerateAllAsync(ServiceBusAdministrationClient admin, Snapshot snap, bool withCounts)
{
    // Collect topic names.
    var topicNames = new List<string>();
    await foreach (var t in admin.GetTopicsAsync())
    {
        topicNames.Add(t.Name);
    }

    // Subs per topic in parallel.
    var topicTasks = topicNames.Select(name => LoadTopicAsync(admin, name, withCounts)).ToArray();
    var topics = await Task.WhenAll(topicTasks);
    snap.Topics.AddRange(topics);

    // Queues.
    await foreach (var q in admin.GetQueuesAsync())
    {
        snap.Queues.Add(await BuildQueueEntryAsync(admin, q.Name, withCounts));
    }
}

static async Task EnumerateFiltersAsync(ServiceBusAdministrationClient admin, Snapshot snap, List<string> filters, bool withCounts)
{
    foreach (var filter in filters)
    {
        var slashIdx = filter.IndexOf('/');
        if (slashIdx > 0)
        {
            // Explicit topic/sub.
            var topic = filter[..slashIdx];
            var sub = filter[(slashIdx + 1)..];
            var topicEntry = snap.Topics.FirstOrDefault(t => t.Name == topic);
            if (topicEntry is null)
            {
                topicEntry = new TopicEntry { Name = topic };
                snap.Topics.Add(topicEntry);
            }
            try
            {
                topicEntry.Subscriptions.Add(await BuildSubEntryAsync(admin, topic, sub, withCounts));
            }
            catch (Exception ex)
            {
                topicEntry.Subscriptions.Add(new SubscriptionEntry { Name = sub, Error = Truncate(ex.Message) });
            }
        }
        else
        {
            // Could be a topic OR a queue — try topic first.
            if (await admin.TopicExistsAsync(filter))
            {
                snap.Topics.Add(await LoadTopicAsync(admin, filter, withCounts));
            }
            else if (await admin.QueueExistsAsync(filter))
            {
                snap.Queues.Add(await BuildQueueEntryAsync(admin, filter, withCounts));
            }
            else
            {
                snap.Topics.Add(new TopicEntry { Name = filter, Error = "not found as topic or queue" });
            }
        }
    }
}

static async Task<TopicEntry> LoadTopicAsync(ServiceBusAdministrationClient admin, string name, bool withCounts)
{
    var entry = new TopicEntry { Name = name };
    try
    {
        await foreach (var s in admin.GetSubscriptionsAsync(name))
        {
            entry.Subscriptions.Add(await BuildSubEntryAsync(admin, name, s.SubscriptionName, withCounts));
        }
    }
    catch (Exception ex)
    {
        entry.Error = Truncate(ex.Message);
    }
    return entry;
}

static async Task<SubscriptionEntry> BuildSubEntryAsync(ServiceBusAdministrationClient admin, string topic, string sub, bool withCounts)
{
    if (!withCounts)
    {
        return new SubscriptionEntry { Name = sub };
    }
    var runtime = (await admin.GetSubscriptionRuntimePropertiesAsync(topic, sub)).Value;
    return new SubscriptionEntry
    {
        Name = sub,
        Active = runtime.ActiveMessageCount,
        Dlq = runtime.DeadLetterMessageCount,
    };
}

static async Task<QueueEntry> BuildQueueEntryAsync(ServiceBusAdministrationClient admin, string name, bool withCounts)
{
    if (!withCounts)
    {
        return new QueueEntry { Name = name };
    }
    var runtime = (await admin.GetQueueRuntimePropertiesAsync(name)).Value;
    return new QueueEntry
    {
        Name = name,
        Active = runtime.ActiveMessageCount,
        Dlq = runtime.DeadLetterMessageCount,
        SizeBytes = runtime.SizeInBytes,
    };
}

static string Truncate(string s) => s.Length > 200 ? s[..200] + "..." : s;

static void PrintHierarchicalText(Snapshot snap)
{
    var sb = new StringBuilder();
    sb.Append("NAMESPACE: ").Append(snap.Namespace)
      .Append("  (discovered ").Append(snap.DiscoveredAt.ToString("o", CultureInfo.InvariantCulture)).Append(')').AppendLine();
    if (snap.WithCounts) sb.AppendLine("            (with --with-counts — every entity includes active/dlq)");
    sb.AppendLine();

    sb.Append("TOPICS (").Append(snap.Topics.Count).Append("):").AppendLine();
    foreach (var t in snap.Topics)
    {
        sb.Append("  ").Append(t.Name);
        if (t.Error is not null) sb.Append("  [ERROR: ").Append(t.Error).Append(']');
        sb.AppendLine();
        if (t.Subscriptions.Count > 0)
        {
            sb.Append("    subscriptions (").Append(t.Subscriptions.Count).Append("):").AppendLine();
            foreach (var s in t.Subscriptions)
            {
                sb.Append("      ").Append(s.Name);
                if (s.Active is not null || s.Dlq is not null)
                {
                    sb.Append("  (active=").Append(s.Active?.ToString(CultureInfo.InvariantCulture) ?? "?")
                      .Append(", dlq=").Append(s.Dlq?.ToString(CultureInfo.InvariantCulture) ?? "?")
                      .Append(')');
                }
                if (s.Error is not null) sb.Append("  [ERROR: ").Append(s.Error).Append(']');
                sb.AppendLine();
            }
        }
    }
    sb.AppendLine();

    sb.Append("QUEUES (").Append(snap.Queues.Count).Append("):").AppendLine();
    foreach (var q in snap.Queues)
    {
        sb.Append("  ").Append(q.Name);
        if (q.Active is not null || q.Dlq is not null || q.SizeBytes is not null)
        {
            sb.Append("  (active=").Append(q.Active?.ToString(CultureInfo.InvariantCulture) ?? "?")
              .Append(", dlq=").Append(q.Dlq?.ToString(CultureInfo.InvariantCulture) ?? "?");
            if (q.SizeBytes is long bytes) sb.Append(", size=").Append(bytes.ToString("N0", CultureInfo.InvariantCulture)).Append('B');
            sb.Append(')');
        }
        sb.AppendLine();
    }

    Console.Write(sb.ToString());
}

internal sealed record Snapshot
{
    public required string Namespace { get; init; }
    public required DateTimeOffset DiscoveredAt { get; init; }
    public required bool WithCounts { get; init; }
    public List<TopicEntry> Topics { get; } = new();
    public List<QueueEntry> Queues { get; } = new();
}

internal sealed record TopicEntry
{
    public required string Name { get; init; }
    public List<SubscriptionEntry> Subscriptions { get; } = new();
    public string? Error { get; set; }
}

internal sealed record SubscriptionEntry
{
    public required string Name { get; init; }
    public long? Active { get; init; }
    public long? Dlq { get; init; }
    public string? Error { get; init; }
}

internal sealed record QueueEntry
{
    public required string Name { get; init; }
    public long? Active { get; init; }
    public long? Dlq { get; init; }
    public long? SizeBytes { get; init; }
}
