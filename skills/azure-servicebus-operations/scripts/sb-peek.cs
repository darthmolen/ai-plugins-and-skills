#:package Azure.Messaging.ServiceBus@7.18.4
#:package Azure.Identity@1.13.2

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Azure.Identity;
using Azure.Messaging.ServiceBus;

#pragma warning disable IL2026 // JsonSerializer trim warning — single-file script, not trimmed
#pragma warning disable IL3050 // JsonSerializer AOT warning — single-file script, not AOT-compiled

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: dotnet run sb-peek.cs <namespace> <topic-or-queue> <subscription-or-empty> [count] [--subqueue=main|deadletter] [--write-messages=true|false] [--out=DIR]");
    Console.Error.WriteLine("  namespace          : short name (sbns-delta-alpha-prod) or FQDN");
    Console.Error.WriteLine("  topic-or-queue     : topic name (sub mode) OR queue name (when subscription arg is empty string).");
    Console.Error.WriteLine("  subscription       : subscription name OR empty string \"\" to peek a queue directly.");
    Console.Error.WriteLine("  count              : messages to peek (default 1, non-destructive)");
    Console.Error.WriteLine("  --subqueue         : main (default) or deadletter — which sub-queue to peek.");
    Console.Error.WriteLine("  --write-messages   : when true, dump each peeked message to disk as JSON (metadata + body). Default false.");
    Console.Error.WriteLine("  --out              : output directory root (default ./peek-output). Each peek run writes to {out}/{entity-name}/.");
    return 1;
}

string ns = args[0];
string topic = args[1];
string sub = args[2];
int count = 1;
bool writeMessages = false;
string outRoot = "./peek-output";
SubQueue subQueueKind = SubQueue.None;

foreach (var arg in args.Skip(3))
{
    if (arg.StartsWith("--write-messages=", StringComparison.Ordinal))
    {
        writeMessages = bool.Parse(arg["--write-messages=".Length..]);
    }
    else if (arg.StartsWith("--out=", StringComparison.Ordinal))
    {
        outRoot = arg["--out=".Length..];
    }
    else if (arg.StartsWith("--subqueue=", StringComparison.Ordinal))
    {
        var v = arg["--subqueue=".Length..].ToLowerInvariant();
        subQueueKind = v switch
        {
            "main" or "" => SubQueue.None,
            "deadletter" or "dlq" or "dead-letter" => SubQueue.DeadLetter,
            _ => throw new ArgumentException($"--subqueue must be 'main' or 'deadletter', got '{v}'"),
        };
    }
    else if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c))
    {
        count = c;
    }
    else
    {
        Console.Error.WriteLine($"unknown arg: {arg}");
        return 1;
    }
}

bool queueMode = string.IsNullOrEmpty(sub);
string entityLabel = queueMode ? topic : $"{topic}/{sub}";
string subQueueLabel = subQueueKind == SubQueue.DeadLetter ? "/$DeadLetterQueue" : "";

string fqdn = ns.Contains('.') ? ns : $"{ns}.servicebus.windows.net";
Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] connecting {fqdn} / {entityLabel}{subQueueLabel} target={count} write={writeMessages} (peek-only, non-destructive)");

string? outDir = null;
if (writeMessages)
{
    // Full entity name in the folder (no substring) so each peek goes to its own bucket.
    var folderName = queueMode ? topic : $"{topic}__{sub}";
    if (subQueueKind == SubQueue.DeadLetter) folderName += "__deadletter";
    outDir = Path.Combine(outRoot, folderName);
    Directory.CreateDirectory(outDir);
    Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] writing messages to {Path.GetFullPath(outDir)}");
}

var receiverOptions = new ServiceBusReceiverOptions { SubQueue = subQueueKind };

await using var client = new ServiceBusClient(fqdn, new DefaultAzureCredential());
await using var receiver = queueMode
    ? client.CreateReceiver(topic, receiverOptions)
    : client.CreateReceiver(topic, sub, receiverOptions);

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
};

int peeked = 0;
var start = DateTimeOffset.UtcNow;

while (peeked < count)
{
    int batchSize = Math.Min(count - peeked, 250);
    IReadOnlyList<ServiceBusReceivedMessage> batch = await receiver.PeekMessagesAsync(batchSize);

    if (batch.Count == 0)
    {
        Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] peek returned 0 messages (end of queue); stopping at {peeked}");
        break;
    }

    foreach (var msg in batch)
    {
        peeked++;

        // When writing, log every message (user is inspecting); otherwise keep the original first-5-or-100th cadence.
        if (writeMessages || peeked <= 5 || peeked % 100 == 0)
        {
            var routeName = msg.ApplicationProperties.TryGetValue("RouteName", out var rn) ? rn?.ToString() : null;
            var dlqExtra = msg.DeadLetterReason is not null || msg.DeadLetterErrorDescription is not null
                ? $" dlqReason={msg.DeadLetterReason ?? "(null)"} dlqDesc={Truncate(msg.DeadLetterErrorDescription, 200)}"
                : "";
            var routeExtra = routeName is not null ? $" route={routeName}" : "";
            Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] peek #{peeked} seq={msg.SequenceNumber} enq={msg.EnqueuedTime:o} bodyBytes={msg.Body.ToMemory().Length}{routeExtra}{dlqExtra}");
        }

        if (writeMessages && outDir is not null)
        {
            // Filename: enq-{iso-utc}_seq-{n}.json — lexicographic sort == chronological sort, latest at the bottom.
            var safeEnq = msg.EnqueuedTime.UtcDateTime.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
            var filename = $"enq-{safeEnq}_seq-{msg.SequenceNumber}.json";
            var path = Path.Combine(outDir, filename);

            var record = BuildRecord(msg, jsonOptions);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record, jsonOptions));
        }
    }
}

var elapsed = DateTimeOffset.UtcNow - start;
double secs = Math.Max(0.001, elapsed.TotalSeconds);
Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] DONE peeked={peeked} elapsed={secs:F1}s rate={peeked / secs:F1}/s");
if (writeMessages && outDir is not null)
{
    Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] {peeked} message(s) written to {Path.GetFullPath(outDir)}");
}
return 0;

static string Truncate(string? s, int max)
{
    if (string.IsNullOrEmpty(s)) return "(null)";
    var oneLine = s.Replace('\r', ' ').Replace('\n', ' ');
    return oneLine.Length <= max ? oneLine : oneLine[..max] + "...";
}

static object BuildRecord(ServiceBusReceivedMessage msg, JsonSerializerOptions jsonOptions)
{
    // Try to parse the body as JSON for readability; fall back to raw text; final fallback to base64.
    object? bodyParsed = null;
    string? bodyText = null;
    string? bodyBase64 = null;
    var bytes = msg.Body.ToArray();

    try
    {
        // JsonNode is a DOM type with native serializer support — survives reflection-disabled runtimes
        // and lets the outer Serialize call emit it as nested JSON in the record.
        bodyParsed = System.Text.Json.Nodes.JsonNode.Parse(bytes);
    }
    catch (JsonException)
    {
        try
        {
            bodyText = System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            bodyBase64 = Convert.ToBase64String(bytes);
        }
    }

    return new
    {
        sequenceNumber = msg.SequenceNumber,
        messageId = msg.MessageId,
        correlationId = msg.CorrelationId,
        sessionId = msg.SessionId,
        subject = msg.Subject,
        contentType = msg.ContentType,
        enqueuedTime = msg.EnqueuedTime,
        expiresAt = msg.ExpiresAt,
        timeToLive = msg.TimeToLive,
        deliveryCount = msg.DeliveryCount,
        deadLetterReason = msg.DeadLetterReason,
        deadLetterErrorDescription = msg.DeadLetterErrorDescription,
        deadLetterSource = msg.DeadLetterSource,
        applicationProperties = msg.ApplicationProperties.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString()),
        bodyParsedJson = bodyParsed,
        bodyText = bodyText,
        bodyBase64 = bodyBase64,
        bodyBytes = bytes.Length,
    };
}
