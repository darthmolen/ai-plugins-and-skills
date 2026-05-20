#:package Azure.Messaging.ServiceBus@7.18.4
#:package Azure.Identity@1.13.2

using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;

// Clears the ForwardTo property on a topic subscription.
// The az CLI rejects --forward-to "" with a validation error, so we go direct to the
// ServiceBusAdministrationClient SDK which accepts null on UpdateSubscriptionAsync.

if (args.Length < 3 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("Usage: dotnet run sb-clear-fwd.cs <namespace> <topic> <subscription>");
    return 1;
}

string ns = args[0];
string topic = args[1];
string sub = args[2];

string fqdn = ns.Contains('.') ? ns : $"{ns}.servicebus.windows.net";
Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] clearing ForwardTo on {fqdn} / {topic} / {sub}");

var admin = new ServiceBusAdministrationClient(fqdn, new DefaultAzureCredential());

var props = (await admin.GetSubscriptionAsync(topic, sub)).Value;
Console.WriteLine($"  before: ForwardTo='{props.ForwardTo}'");

props.ForwardTo = null;
var updated = (await admin.UpdateSubscriptionAsync(props)).Value;
Console.WriteLine($"  after:  ForwardTo='{updated.ForwardTo}'");

Console.WriteLine($"[{DateTimeOffset.UtcNow:o}] done");
return 0;
