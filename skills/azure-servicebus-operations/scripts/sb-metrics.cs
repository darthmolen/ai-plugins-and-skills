#:package Azure.Messaging.ServiceBus@7.18.4
#:package Azure.Identity@1.13.2

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;

#pragma warning disable IL2026 // JsonSerializer trim warning — single-file script, not trimmed
#pragma warning disable IL3050 // JsonSerializer AOT warning — single-file script, not AOT-compiled

// Snapshot counts (and FwdTo for subscriptions) on one or more Service Bus entities in one call.
// Replaces the recurring `az servicebus ... show --query "{active:..., dlq:..., ...}"` patterns.
//
// Entity format:
//   queueName              -> treated as a queue
//   topicName/subName      -> treated as a topic subscription (slash-separated)

if (args.Length < 2 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("Usage: dotnet run sb-metrics.cs <namespace> <entity> [entity ...] [--json]");
    Console.Error.WriteLine("  entity:");
    Console.Error.WriteLine("    queueName              treated as a queue");
    Console.Error.WriteLine("    topicName/subName      treated as a topic subscription");
    Console.Error.WriteLine("  --json                   one JSON object per entity (newline-delimited) instead of an ASCII table");
    return 1;
}

string ns = args[0];
bool json = false;
var entityArgs = new List<string>();

foreach (var arg in args.Skip(1))
{
    if (arg == "--json") json = true;
    else if (arg.StartsWith("--", StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"unknown flag: {arg}");
        return 1;
    }
    else entityArgs.Add(arg);
}

if (entityArgs.Count == 0)
{
    Console.Error.WriteLine("no entities specified");
    return 1;
}

string fqdn = ns.Contains('.') ? ns : $"{ns}.servicebus.windows.net";
var admin = new ServiceBusAdministrationClient(fqdn, new DefaultAzureCredential());

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = false,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
};

// Fetch all entities in parallel.
var tasks = entityArgs.Select(e => FetchAsync(admin, e)).ToArray();
var results = await Task.WhenAll(tasks);

if (json)
{
    foreach (var r in results)
    {
        Console.WriteLine(JsonSerializer.Serialize(r, jsonOptions));
    }
}
else
{
    PrintTable(results);
}

return 0;

static async Task<EntityMetric> FetchAsync(ServiceBusAdministrationClient admin, string entity)
{
    var slashIdx = entity.IndexOf('/');
    try
    {
        if (slashIdx > 0)
        {
            var topic = entity[..slashIdx];
            var sub = entity[(slashIdx + 1)..];
            var runtime = (await admin.GetSubscriptionRuntimePropertiesAsync(topic, sub)).Value;
            var props = (await admin.GetSubscriptionAsync(topic, sub)).Value;
            return new EntityMetric
            {
                Entity = entity,
                Kind = "subscription",
                Active = runtime.ActiveMessageCount,
                Dlq = runtime.DeadLetterMessageCount,
                Scheduled = runtime.TotalMessageCount - runtime.ActiveMessageCount - runtime.DeadLetterMessageCount - runtime.TransferDeadLetterMessageCount,
                Transfer = null,
                TransferDlq = runtime.TransferDeadLetterMessageCount,
                SizeBytes = null,
                FwdTo = string.IsNullOrEmpty(props.ForwardTo) ? null : props.ForwardTo,
            };
        }
        else
        {
            var runtime = (await admin.GetQueueRuntimePropertiesAsync(entity)).Value;
            return new EntityMetric
            {
                Entity = entity,
                Kind = "queue",
                Active = runtime.ActiveMessageCount,
                Dlq = runtime.DeadLetterMessageCount,
                Scheduled = runtime.ScheduledMessageCount,
                Transfer = runtime.TransferMessageCount,
                TransferDlq = runtime.TransferDeadLetterMessageCount,
                SizeBytes = runtime.SizeInBytes,
                FwdTo = null,
            };
        }
    }
    catch (Exception ex)
    {
        return new EntityMetric
        {
            Entity = entity,
            Kind = slashIdx > 0 ? "subscription" : "queue",
            Error = ex.GetType().Name + ": " + (ex.Message.Length > 200 ? ex.Message[..200] + "..." : ex.Message),
        };
    }
}

static void PrintTable(EntityMetric[] rows)
{
    // Column widths.
    int wEntity = Math.Max(6, rows.Max(r => r.Entity.Length));
    const int wActive = 8, wDlq = 10, wSched = 7, wSize = 10, wFwd = 60;

    var sb = new StringBuilder();
    sb.Append("ENTITY".PadRight(wEntity)).Append("  ")
      .Append("ACTIVE".PadRight(wActive)).Append("  ")
      .Append("DLQ".PadRight(wDlq)).Append("  ")
      .Append("SCHED".PadRight(wSched)).Append("  ")
      .Append("SIZE".PadRight(wSize)).Append("  ")
      .Append("FWD_TO").AppendLine();
    sb.Append(new string('-', wEntity + wActive + wDlq + wSched + wSize + wFwd + 10)).AppendLine();

    foreach (var r in rows)
    {
        if (r.Error is not null)
        {
            sb.Append(r.Entity.PadRight(wEntity)).Append("  ")
              .Append("ERROR: ").Append(r.Error).AppendLine();
            continue;
        }
        sb.Append(r.Entity.PadRight(wEntity)).Append("  ")
          .Append((r.Active?.ToString(CultureInfo.InvariantCulture) ?? "-").PadRight(wActive)).Append("  ")
          .Append((r.Dlq?.ToString(CultureInfo.InvariantCulture) ?? "-").PadRight(wDlq)).Append("  ")
          .Append((r.Scheduled?.ToString(CultureInfo.InvariantCulture) ?? "-").PadRight(wSched)).Append("  ")
          .Append((r.SizeBytes is long bytes ? FormatBytes(bytes) : "-").PadRight(wSize)).Append("  ")
          .Append(StripFwdToHost(r.FwdTo) ?? "-")
          .AppendLine();
    }

    Console.Write(sb.ToString());
}

static string FormatBytes(long bytes)
{
    if (bytes < 1024) return $"{bytes} B";
    if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
    if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024:F1} MB";
    return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
}

static string? StripFwdToHost(string? fwdTo)
{
    // ServiceBusAdministrationClient returns FwdTo as a full URL (sb://host/path).
    // Strip to just the entity name for table readability.
    if (fwdTo is null) return null;
    var lastSlash = fwdTo.LastIndexOf('/');
    return lastSlash >= 0 && lastSlash < fwdTo.Length - 1 ? fwdTo[(lastSlash + 1)..] : fwdTo;
}

internal sealed record EntityMetric
{
    public required string Entity { get; init; }
    public required string Kind { get; init; }
    public long? Active { get; init; }
    public long? Dlq { get; init; }
    public long? Scheduled { get; init; }
    public long? Transfer { get; init; }
    public long? TransferDlq { get; init; }
    public long? SizeBytes { get; init; }
    public string? FwdTo { get; init; }
    public string? Error { get; init; }
}
