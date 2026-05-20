#:package Azure.Messaging.ServiceBus@7.18.4
#:package Azure.Identity@1.13.2

using System.Globalization;
using Azure.Identity;
using Azure.Messaging.ServiceBus;

// DESTRUCTIVE — receives messages from a Service Bus subscription in ReceiveAndDelete mode.
// Requires --confirm=true to actually run. Without --confirm, the script refuses.
//
// Intended for one-off cleanups where the messages have been captured elsewhere (see sb-peek.cs)
// and the live copies need to go away (e.g. parked alerts whose root cause is already tracked).

if (args.Length < 3 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("Usage: dotnet run sb-drain.cs <namespace> <topic> <subscription> --confirm=true [--max=N]");
    Console.Error.WriteLine("  --confirm : MUST be 'true' to actually drain. Without it the script refuses (safety).");
    Console.Error.WriteLine("  --max     : safety cap on messages drained (default 1000).");
    return 1;
}

string ns = args[0];
string topic = args[1];
string sub = args[2];
bool confirm = false;
int max = 1000;

foreach (var arg in args.Skip(3))
{
    if (arg.StartsWith("--confirm=", StringComparison.Ordinal)) confirm = bool.Parse(arg["--confirm=".Length..]);
    else if (arg.StartsWith("--max=", StringComparison.Ordinal)) max = int.Parse(arg["--max=".Length..], CultureInfo.InvariantCulture);
    else { Console.Error.WriteLine($"unknown arg: {arg}"); return 1; }
}

if (!confirm)
{
    Console.Error.WriteLine("Refusing to drain — pass --confirm=true to acknowledge messages will be permanently deleted.");
    return 1;
}

string fqdn = ns.Contains('.') ? ns : $"{ns}.servicebus.windows.net";
Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] DRAIN (ReceiveAndDelete) {fqdn} / {topic} / {sub} max={max}");

await using var client = new ServiceBusClient(fqdn, new DefaultAzureCredential());
await using var receiver = client.CreateReceiver(
    topic,
    sub,
    new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

int drained = 0;
var start = DateTimeOffset.UtcNow;

while (drained < max)
{
    var msg = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
    if (msg is null)
    {
        Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] receiver returned null after 5s wait — subscription empty, stopping at {drained}");
        break;
    }
    drained++;
    Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] drained #{drained} seq={msg.SequenceNumber} enq={msg.EnqueuedTime:o} bodyBytes={msg.Body.ToMemory().Length}");
}

var elapsed = DateTimeOffset.UtcNow - start;
Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] DONE drained={drained} elapsed={elapsed.TotalSeconds:F1}s");
return 0;
