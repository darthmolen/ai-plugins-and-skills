---
name: azure-servicebus-operations
description: Use when peeking, draining, snapshotting metrics on, enumerating, or otherwise inspecting/manipulating Azure Service Bus subscriptions, queues, or dead-letter sub-queues — including diagnosing DLQ accumulation, capturing parked messages for forensics before discard, draining stuck alert backlogs, comparing active/dlq counts across multiple subs, enumerating an entire namespace's topology, or clearing a subscription's ForwardTo (which the az CLI cannot do).
metadata:
  category: tools
---
# Azure Service Bus Operations (Single-File C# Scripts)

## Overview
Six dotnet single-file scripts bundled at `scripts/`. They handle operational Service Bus tasks the az CLI can't do (or makes painful). All authenticate via `DefaultAzureCredential` — `az login` covers the common case.

## When to use which

| Symptom / task                                                       | Script        | Destructive?         |
|----------------------------------------------------------------------|---------------|----------------------|
| "List every topic / queue / subscription in this namespace"          | sb-discovery  | No                   |
| "Quick snapshot of active + DLQ counts on one or more entities"      | sb-metrics    | No                   |
| "What's in this subscription / queue / DLQ?"                         | sb-peek       | No                   |
| "I need each message dumped to disk as JSON to inspect"              | sb-peek --write-messages=true | No   |
| "Permanently discard everything in this sub or DLQ"                  | sb-drain      | YES (--confirm=true) |
| "az CLI rejects --forward-to '' with LinkedInvalidPropertyId"        | sb-clear-fwd  | Yes (config)         |
| "Receive+complete N messages to smoke-test a consumer"               | sb-consume    | YES                  |

## Prerequisites
- dotnet 10+ SDK (single-file script support; `dotnet run script.cs`)
- `az login` with reader role (peek / consume / drain / metrics / discovery) or contributor (clear-fwd) on the SB namespace
- Service Bus namespace can be short name (`sbns-foo-prod`) or FQDN

## Convenience: path variable

```powershell
# Set once per session — works whether installed via plugin or as a user skill
$sb = if ($env:CLAUDE_PLUGIN_ROOT) {
  "$env:CLAUDE_PLUGIN_ROOT\skills\azure-servicebus-operations\scripts"
} else {
  "$HOME\.claude\skills\azure-servicebus-operations\scripts"
}
```

## sb-discovery
Enumerate Service Bus entities. Default hierarchical text view; `--json` for machine-readable; `--out=FILE` writes the JSON for later reference. Optional positional entity args narrow scope (auto-detected as topic, queue, or topic/sub).

```powershell
# Shotgun the whole namespace, save snapshot for later questions
dotnet run $sb\sb-discovery.cs sbns-delta-alpha-prod --json --out=topology.json

# Narrow to one topic + its subs, with counts (fast because scope is small)
dotnet run $sb\sb-discovery.cs sbns-delta-alpha-prod dell-b2b-messages --with-counts

# Mix topic + queue in one call
dotnet run $sb\sb-discovery.cs sbns-delta-alpha-prod dell-b2b-messages dell-b2b_admin-message_default
```

`--with-counts` adds active+dlq numbers per entity (slower; pairs well with narrowed args).

## sb-metrics
Snapshot counts + FwdTo for one or more entities in a single call. Default ASCII table; `--json` for newline-delimited JSON per entity. Entity is either `queueName` or `topicName/subscription`.

```powershell
dotnet run $sb\sb-metrics.cs sbns-delta-alpha-prod `
  dell-b2b_admin-message_default `
  dell-b2b-messages/admin-message_ConnectorRequestNotification `
  dell-b2b-messages/admin-message_ConnectorResponseNotification `
  dell-b2b-messages/admin-message_SendAlertCommand
```

## sb-peek
Non-destructive peek. Pass `""` for the subscription arg to peek a queue directly. `--subqueue=deadletter` targets a DLQ sub-queue. `--write-messages=true` dumps each message to disk as JSON.

```powershell
# Capture all DLQ entries on a queue to disk for forensics
dotnet run $sb\sb-peek.cs sbns-delta-alpha-prod dell-b2b_admin-message_default "" 1100 --subqueue=deadletter --write-messages=true
```

## sb-drain
Destructive (`ReceiveAndDelete`). Refuses without `--confirm=true`. `--max=N` caps drained count (default 1000). **Always pair with `sb-peek --write-messages=true` first if forensics matter** — drained messages are unrecoverable.

```powershell
dotnet run $sb\sb-drain.cs sbns-delta-alpha-prod dell-b2b-messages admin-message_SendAlertCommand --confirm=true --max=200
```

## sb-clear-fwd
The az CLI rejects `--forward-to ""` with `LinkedInvalidPropertyId`. This script clears `ForwardTo` via `ServiceBusAdministrationClient.UpdateSubscriptionAsync` (which accepts null).

```powershell
dotnet run $sb\sb-clear-fwd.cs sbns-delta-alpha-prod dell-b2b-messages orders-message_RealtimeMessageReceivedNotification
```

## sb-consume
Destructive `PeekLock` + `CompleteMessageAsync`. Use for consumer-pipeline smoke tests where each message is acked individually (rather than batch-deleted).

```powershell
dotnet run $sb\sb-consume.cs sbns-delta-alpha-prod dell-b2b-messages orders-message_SomeRoute 50
```

## Memory integration

After running `sb-discovery --out=FILE`, save a Claude reference-memory entry pointing at the file so future questions can be answered against the snapshot without re-querying:

```
- [SB topology snapshot — sbns-delta-alpha-prod 2026-05-17](reference_sb_topology_alpha_prod_20260517.md) — pointer to JSON; refresh with sb-discovery before relying on it.
```

## Common mistakes
- Forgetting `--confirm=true` on sb-drain → script refuses (intentional safety).
- Passing `--forward-to ""` to az CLI → `LinkedInvalidPropertyId`. Use sb-clear-fwd.
- Peeking a queue but supplying a sub name → pass `""` for the subscription arg to enter queue mode.
- Running sb-drain against an actively-consumed sub → drains messages out from under the live consumer.
- Forgetting DLQ is a sub-queue → use `sb-peek --subqueue=deadletter`, not the main view.
- Running `sb-discovery --with-counts` against a huge namespace without narrowing → one round-trip per entity, slow. Narrow first.

## Real-world example
SendAlertCommand cleanup pre-relaunch:
1. `sb-metrics` over the constellation → confirmed 87 parked on the sub, 1,049 on the pool queue DLQ.
2. `sb-peek --write-messages=true 200` → captured 87 alerts to disk for forensics; sorted disk files by filename to find the most-recent.
3. `sb-drain --confirm=true --max=200` → destructively cleared the sub.
4. `sb-metrics` again → confirmed `active=0, dlq=0` before re-enabling the consumer in the next deploy.
