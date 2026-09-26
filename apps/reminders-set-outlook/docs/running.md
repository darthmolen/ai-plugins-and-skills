# Running it on your machine

The recommended setup is: **publish once, sign in once, then have the app start at log-on in
`watch` mode.** It scans every 15 minutes, the scan is cheap (it reads a handful of Markdown
files and only calls Graph when a hash changed), and if a sign-in is ever needed the browser
opens at log-on — when you are there to answer it.

## 1. Build and publish

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
cd apps\reminders-set-outlook\src
dotnet test --solution RemindersSetOutlook.slnx
dotnet publish RemindersSetOutlook\RemindersSetOutlook.csproj -c Release -r win-x64 --self-contained false -o $env:LOCALAPPDATA\Programs\reminders-set-outlook
```

Use `-r osx-arm64` or `-r linux-x64` elsewhere. Add `--self-contained true` if the machine it
runs on will not have the .NET 10 runtime.

## 2. Configure

Settings are layered, later winning:

1. `appsettings.json` beside the executable — the shipped defaults; do not edit it, a
   re-publish overwrites it.
2. **Your user config file** — put your settings here:
   - Windows: `%APPDATA%\reminders-set-outlook\appsettings.json`
   - macOS and Linux: `~/.config/reminders-set-outlook/appsettings.json`
     (or under `$XDG_CONFIG_HOME` when that is set)
3. Environment variables prefixed `REMINDERS_OUTLOOK_`, with `__` for `:` —
   `REMINDERS_OUTLOOK_Todo__ListName=Work`.
4. Command-line overrides — `--Todo:ListName=Work`.

A minimal user config:

```json
{
  "Scan": {
    "Roots": [ "C:\\src", "D:\\work" ]
  },
  "Auth": {
    "ClientId": "<from the app registration>",
    "TenantId": "consumers"
  }
}
```

`Scan:Roots` is the top of the folder hierarchy — usually the folder all your clones live
under. Everything matching `**/planning/reminders/**/*.md` beneath it is scanned, with
`.git`, `node_modules`, `bin` and `obj` skipped. See the [app README](../README.md#configuration)
for every setting.

Check what it would do before it does anything:

```powershell
reminders-set-outlook sync --dry-run
```

## 3. Sign in once

```powershell
reminders-set-outlook login
```

The browser opens; sign in and accept the `Tasks.ReadWrite` consent. The details are in
[authentication.md](authentication.md).

## 4. Start it at log-on

### Windows — Task Scheduler (recommended)

Run this once in PowerShell (not elevated; it is a task for your own account):

```powershell
$exe     = "$env:LOCALAPPDATA\Programs\reminders-set-outlook\reminders-set-outlook.exe"
$action  = New-ScheduledTaskAction -Execute 'conhost.exe' -Argument "--headless `"$exe`" watch"
$trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 5) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName 'reminders-set-outlook' `
    -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
    -Description 'Mirrors reminders-set files into Microsoft To Do'

Start-ScheduledTask -TaskName 'reminders-set-outlook'
```

What those choices do:

- **`-LogonType Interactive`** is *run only when the user is logged on*. The process runs in
  your desktop session, so if a sign-in is needed the browser prompt appears in front of you.
- **`conhost.exe --headless`** keeps the console window from sitting on your taskbar all day
  (Windows 10 1809 and later). The log file below replaces it.
- **`ExecutionTimeLimit` zero** stops Task Scheduler killing `watch` after its default 72 hours.
- **`RestartCount`** brings it back if it exits with an error.

To stop or remove it: `Stop-ScheduledTask` / `Unregister-ScheduledTask -TaskName
'reminders-set-outlook'`.

**Prefer short runs to a resident process?** Replace the action's `watch` with `sync` and add
a repetition to the trigger:

```powershell
$trigger.Repetition = (New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 30)).Repetition
```

Two runs never overlap — a lock file makes the second one exit with code 4.

### macOS — launchd

`~/Library/LaunchAgents/com.darthmolen.reminders-set-outlook.plist`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.darthmolen.reminders-set-outlook</string>
  <key>ProgramArguments</key>
  <array>
    <string>/Users/you/Applications/reminders-set-outlook/reminders-set-outlook</string>
    <string>watch</string>
  </array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><dict><key>SuccessfulExit</key><false/></dict>
</dict>
</plist>
```

`launchctl load ~/Library/LaunchAgents/com.darthmolen.reminders-set-outlook.plist`

### Linux — systemd user service

`~/.config/systemd/user/reminders-set-outlook.service`:

```ini
[Unit]
Description=Mirror reminders-set files into Microsoft To Do

[Service]
ExecStart=%h/.local/bin/reminders-set-outlook/reminders-set-outlook watch
Restart=on-failure

[Install]
WantedBy=default.target
```

`systemctl --user enable --now reminders-set-outlook`. The token cache needs a running secret
service (GNOME Keyring or KWallet); see [authentication.md](authentication.md#what-is-stored-and-where).

## Where things are

The state directory is `%LOCALAPPDATA%\reminders-set-outlook` on Windows
(`~/.local/share/reminders-set-outlook` on Linux, `~/Library/Application Support/reminders-set-outlook`
on macOS), or `State:Directory` if set.

| File | What it is |
|---|---|
| `reminders.csv` | The findings: one row per reminder file, with its hash and task id. Open it in Excel. |
| `auth-record.json` | Which account is signed in (no token). |
| `logs/reminders-set-outlook-YYYYMMDD.log` | One file a day, 14 days kept. |
| `sync.lock` | Present only while a sync runs. |

`reminders-set-outlook status` summarises the CSV and lists anything in `Error` or `Invalid`.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | The sync ran, but at least one task write failed (it is retried next run) |
| 2 | Configuration problem — the message says which setting |
| 3 | Sign-in needed or failed |
| 4 | Another sync was already running |
| 130 | Cancelled with Ctrl+C |

## Troubleshooting

- **Nothing is found.** Run `sync --dry-run` and check `Scan:Roots`. Only files with
  `kind: reminder` in their front matter count, unless `Scan:RequireReminderKind` is `false`.
- **A row says `Invalid`.** The front matter did not parse. The message says why; fix the file
  and the next run picks it up. Its task, if any, is left alone meanwhile.
- **A row says `Error`.** The Graph call failed; the message has Graph's error code. The hash
  is not advanced, so the next run retries.
- **You deleted a task in To Do by mistake.** It is recreated the next time its file changes,
  or straight away with `sync --force`.
- **It keeps asking you to sign in.** Check that the log does not mention the token cache; on
  Linux, that a secret service is running.
