# Isolated Environments

**Load this reference when:** local guidance gives each session its own environment for tests and
end-to-end runs, and you are about to create one or run a test in one.

## What an isolated environment is

Some repositories give each session its own disposable environment for tests and end-to-end runs: a
container stack with its own database, services, ports and front end, and no route to shared systems.
Local guidance names its commands to create, use, list, prune and remove it.

Where it exists, it replaces locks for everything inside it. A port, database or container in the
session's own environment is not shared, so it takes no lock, and two sessions can use the same port
numbers at once. Anything outside it that two sessions can disturb still takes a lock.

The session's bookkeeping for an environment is in `session-workflow`, under *Isolated
environments*: waiting for capacity, recording which environment the session holds, removing it at
every stop, and locking what stays shared. The rules below are about the environment itself.

## Start clean

Before creating one, run the guidance's list and prune commands. Reuse the environment this
session's subject already holds, if it exists.

## Never widen it

Do not publish its ports, attach it to other networks or put real credentials in it to make a test
pass. Each of those gives the test a route to a shared system, and the test then passes against
something another session can change.

A test that needs a real external service is blocked, not fixed. Report it as blocked in the
handover, and name the service.

## Why it is removed at every stop

An environment left behind after its session ends holds a slot in the guidance's claim mechanism,
so the next session waits for capacity nobody is using. It also keeps that session's containers,
volumes and data, which the next session's clean start has to identify as a leftover before it can
prune them.

Another session's environment is never removed silently, because that session may be mid-run. Its
tests fail partway, and the evidence it has not collected yet is lost. Show the caller what is
there and ask, as for a stale lock.
