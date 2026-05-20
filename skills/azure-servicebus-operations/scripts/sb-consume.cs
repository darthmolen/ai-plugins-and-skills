#:package Azure.Messaging.ServiceBus@7.18.4
#:package Azure.Identity@1.13.2

using System.Globalization;
using Azure.Identity;
using Azure.Messaging.ServiceBus;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: dotnet run sb-consume.cs <namespace> <topic> <subscription> [count]");
    Console.Error.WriteLine("  namespace : short name (sbns-delta-alpha-prod) or FQDN");
    Console.Error.WriteLine("  count     : messages to receive+complete (default 1)");
    return 1;
}

string ns = args[0];
string topic = args[1];
string sub = args[2];
int count = args.Length >= 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 1;

string fqdn = ns.Contains('.') ? ns : $"{ns}.servicebus.windows.net";
Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] connecting {fqdn} / {topic} / {sub} target={count}");

await using var client = new ServiceBusClient(fqdn, new DefaultAzureCredential());
await using var receiver = client.CreateReceiver(topic, sub, new ServiceBusReceiverOptions
{
    ReceiveMode = ServiceBusReceiveMode.PeekLock,
    PrefetchCount = 0,
});

int received = 0;
var start = DateTimeOffset.UtcNow;

while (received < count)
{
    var msg = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));
    if (msg is null)
    {
        Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] receive returned null after 10s wait (broker thinks empty); stopping at {received}");
        break;
    }

    await receiver.CompleteMessageAsync(msg);
    received++;

    if (received <= 5 || received % 100 == 0)
    {
        Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] complete #{received} seq={msg.SequenceNumber} enq={msg.EnqueuedTime:o} ttl={msg.TimeToLive} expiresAt={msg.ExpiresAt:o} bodyBytes={msg.Body.ToMemory().Length}");
    }
}

var elapsed = DateTimeOffset.UtcNow - start;
double secs = Math.Max(0.001, elapsed.TotalSeconds);
Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] DONE received={received} elapsed={secs:F1}s rate={received / secs:F1}/s");
return 0;
