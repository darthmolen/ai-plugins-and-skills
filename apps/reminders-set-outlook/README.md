# reminders-set-outlook

The [`reminders-set`](../../skills/reminders-set/) skill writes reminders into
`planning/reminders/` in every project — the tasks a person has to do, that no test will ever
catch. Nothing ever *reminded* you of them. This app does: it scans your folder hierarchy for
those files and mirrors each one into a Microsoft To Do task (which Outlook shows under
**My Day / Tasks**), with an alert when the reminder asks for one.

```
C:\src\**\planning\reminders\*.md ──scan──► reminders.csv (hash, task id) ──Graph──► To Do list "Reminders"
```

- **New file** → a task is created.
- **Changed file** (SHA-256 differs from the last sync) → the same task is updated.
- **Unchanged file** → nothing is sent.
- **Closed** (`status: done` or `dropped`) → the task is completed and its alert switched off.
- **Filed into `completed/`** → recognised as a move, not a new reminder; the task is kept.
- **Deleted file** → marked `Missing` in the CSV; the task is left alone, because a reminder is
  never deleted.
- **Task deleted in To Do** → recreated on the next change, or with `sync --force`.

It is one-way: the files are the source of truth. Ticking a task off in To Do does not close
the reminder file — close it the way the skill describes, and the task follows.

## Quick start

1. [Register an app](docs/authentication.md#one-time-setup-register-the-app) for the
   `Tasks.ReadWrite` permission and note its client id.
2. [Publish, configure and schedule it](docs/running.md).

```powershell
reminders-set-outlook sync --dry-run     # what would change
reminders-set-outlook login              # sign in once
reminders-set-outlook sync               # do it
reminders-set-outlook watch              # keep doing it every 15 minutes
reminders-set-outlook status             # summarise the findings CSV
```

## Asking for a reminder alert

A task is created for every reminder. To be *alerted*, add `remind:` to the front matter; to
give the task a due date, add `due:`.

```markdown
---
kind: reminder
status: open
category: verify
audience: developer
subject: display
date: 2026-09-26
plan: screen-legibility_2026-08-27
remind: 2026-10-01 09:30
due: 2026-10-03
---

# Check every screen on the low-end laptop at 1366x768
```

| `remind:` value | Alert fires at |
|---|---|
| `2026-10-01` | 09:00 that day (`Todo:DefaultReminderTime`) |
| `2026-10-01 09:30` or `2026-10-01T09:30` | 09:30 in `Todo:TimeZone` (the machine's zone when blank) |
| `2026-10-01T09:30:00+02:00` | that instant |

A value that cannot be read does not stop the task being written; it is created without an
alert, and the CSV's `Message` column and the log say why. Remove `remind:` or `due:` later and
the alert or due date is cleared from the task on the next sync.

## What goes on the task

- **Title** — the reminder's `#` heading (the file name when there is none).
- **Body** — the file's full path, every front matter field, then the rest of the Markdown.
- **Categories** — `category`, `audience` and `subject`, so To Do can group by them
  (`Todo:ApplyCategories=false` turns this off).
- **Status, alert, due date** — as above.

## The findings CSV

`reminders.csv` in the state directory holds one row per reminder file found:

`Path, Root, State, Hash, ListId, TaskId, Title, Status, Category, Audience, Subject, Date, Plan, Remind, Due, FirstSeenUtc, LastSeenUtc, LastSyncedUtc, Message`

`State` is `Synced`, `Pending` (found, not yet written), `Invalid` (front matter did not parse),
`Error` (the Graph write failed; retried next run) or `Missing` (the file has gone). `Hash` is
the content that was last written to the task, so a failed write is never mistaken for a
successful one.

## Configuration

Every setting, with its default. Put overrides in your user config file — see
[running.md](docs/running.md#2-configure) for where it lives and how settings layer.

| Setting | Default | Meaning |
|---|---|---|
| `Scan:Roots` | *(required)* | Folders to scan recursively. `~` and `%VARS%` expand. |
| `Scan:Include` | `**/planning/reminders/**/*.md` | Glob patterns, relative to each root. |
| `Scan:Exclude` | `**/.git/**`, `**/node_modules/**`, `**/bin/**`, `**/obj/**` | Never scanned. |
| `Scan:RequireReminderKind` | `true` | Only files with `kind: reminder` count. |
| `Todo:ListName` | `Reminders` | To Do list the tasks go in; created when missing. |
| `Todo:ReminderField` | `remind` | Front matter key that asks for an alert. |
| `Todo:DueField` | `due` | Front matter key for the due date. |
| `Todo:DefaultReminderTime` | `09:00` | Alert time for a date-only `remind:`. |
| `Todo:TimeZone` | *(machine's)* | Windows or IANA zone for times without an offset. |
| `Todo:ApplyCategories` | `true` | Copy category, audience and subject to task categories. |
| `Auth:ClientId` | *(required)* | Your app registration's client id. |
| `Auth:TenantId` | `common` | Tenant id, `organizations`, `consumers` or `common`. |
| `Auth:Mode` | `Browser` | `Browser` or `DeviceCode`. |
| `Auth:AllowInteractive` | `true` | May a sync prompt for sign-in? `false` for unattended runs. |
| `Auth:AllowUnencryptedTokenCache` | `false` | Plain-file token cache when no OS secret store exists. |
| `State:Directory` | per-user local app data | Where the CSV, account record and logs live. |
| `State:CsvFileName` | `reminders.csv` | Findings file name. |
| `Watch:IntervalMinutes` | `15` | Minutes between scans in `watch`. |
| `Serilog:MinimumLevel:Default` | `Information` | `Debug` also logs files skipped for not being reminders. |

## Layout

```
apps/reminders-set-outlook/
├── README.md
├── docs/
│   ├── authentication.md       # the auth investigation and app registration steps
│   └── running.md              # publish, configure, schedule, troubleshoot
└── src/
    ├── RemindersSetOutlook.slnx
    ├── Directory.Build.props   # net10.0, nullable, StyleCop, warnings as errors
    ├── RemindersSetOutlook/
    │   ├── Auth/               # MSAL public client, persistent cache, account record
    │   ├── Cli/                # verbs, flags, exit codes
    │   ├── Configuration/      # options and paths
    │   ├── Scanning/           # globbing, front matter, hashing
    │   ├── State/              # the findings CSV
    │   ├── Sync/               # create / update / move / missing reconciliation
    │   └── Todo/               # task drafts and the Graph REST gateway
    └── RemindersSetOutlook.Tests/
```

## Developing

```bash
cd apps/reminders-set-outlook/src
dotnet build RemindersSetOutlook.slnx
dotnet test --solution RemindersSetOutlook.slnx
```

The code follows [`csharp-quality-developer`](../../skills/csharp-quality-developer/):
StyleCop analyzers run in the build with warnings as errors, C# files are CRLF (enforced by
this folder's `.gitattributes`), and logging uses `[LoggerMessage]`. The tests cover parsing,
hashing, the CSV round trip, reminder and due date handling, the Graph payload, and the sync
engine against an in-memory To Do. The Graph gateway itself is exercised only against the
real service.
