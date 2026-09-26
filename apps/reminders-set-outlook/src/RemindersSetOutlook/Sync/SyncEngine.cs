// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemindersSetOutlook.Configuration;
using RemindersSetOutlook.Scanning;
using RemindersSetOutlook.State;
using RemindersSetOutlook.Todo;

namespace RemindersSetOutlook.Sync;

/// <summary>
/// Reconciles the reminder files on disk with the findings CSV and the To Do tasks:
/// a new file gets a task, a changed hash updates it, a moved file keeps it.
/// </summary>
internal sealed partial class SyncEngine
{
    private readonly Func<IReadOnlyList<ScannedReminder>> scan;
    private readonly ReminderStateStore store;
    private readonly TaskDraftBuilder builder;
    private readonly ITodoGateway gateway;
    private readonly TodoOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<SyncEngine> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncEngine"/> class.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="store">The findings store.</param>
    /// <param name="builder">The task draft builder.</param>
    /// <param name="gateway">The To Do gateway.</param>
    /// <param name="options">The To Do options.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="logger">The logger.</param>
    public SyncEngine(
        ReminderScanner scanner,
        ReminderStateStore store,
        TaskDraftBuilder builder,
        ITodoGateway gateway,
        IOptions<TodoOptions> options,
        TimeProvider clock,
        ILogger<SyncEngine> logger)
        : this((scanner ?? throw new ArgumentNullException(nameof(scanner))).Scan, store, builder, gateway, options, clock, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncEngine"/> class with an arbitrary scan source.
    /// </summary>
    /// <param name="scan">Returns the reminders currently on disk.</param>
    /// <param name="store">The findings store.</param>
    /// <param name="builder">The task draft builder.</param>
    /// <param name="gateway">The To Do gateway.</param>
    /// <param name="options">The To Do options.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="logger">The logger.</param>
    internal SyncEngine(
        Func<IReadOnlyList<ScannedReminder>> scan,
        ReminderStateStore store,
        TaskDraftBuilder builder,
        ITodoGateway gateway,
        IOptions<TodoOptions> options,
        TimeProvider clock,
        ILogger<SyncEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.scan = scan;
        this.store = store;
        this.builder = builder;
        this.gateway = gateway;
        this.options = options.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>
    /// Runs one sync.
    /// </summary>
    /// <param name="dryRun">When <c>true</c>, reports what would happen without writing tasks or the CSV.</param>
    /// <param name="force">When <c>true</c>, rewrites every task whether or not its hash changed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the sync did.</returns>
    public async Task<SyncReport> RunAsync(bool dryRun, bool force, CancellationToken cancellationToken)
    {
        var now = this.clock.GetUtcNow();
        var report = new SyncReport { DryRun = dryRun };
        var records = this.store.Load();
        var scanned = this.scan();
        report.Found = scanned.Count;

        var scannedPaths = new HashSet<string>(scanned.Select(reminder => reminder.Path), PathComparer.Default);
        var vanished = records.Values
            .Where(record => !scannedPaths.Contains(record.Path))
            .ToList();

        try
        {
            string? listId = null;
            foreach (var reminder in scanned)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = this.Resolve(records, vanished, reminder, report);
                record.Root = reminder.Root;
                record.LastSeenUtc = now;
                record.FirstSeenUtc ??= now;

                if (reminder.Document is null)
                {
                    record.State = RecordState.Invalid;
                    record.Message = reminder.Error ?? "Could not parse the file.";
                    report.Invalid++;
                    this.LogInvalid(reminder.Path, record.Message);
                    continue;
                }

                this.CopyFields(record, reminder.Document);
                var hasTask = record.TaskId.Length > 0;
                var needsWrite = force || !hasTask || record.Hash != reminder.Hash || record.State != RecordState.Synced;
                if (!needsWrite)
                {
                    report.Unchanged++;
                    continue;
                }

                if (dryRun)
                {
                    if (hasTask)
                    {
                        report.Updated++;
                    }
                    else
                    {
                        report.Created++;
                        record.State = RecordState.Pending;
                    }

                    this.LogWouldWrite(hasTask ? "update" : "create", reminder.Path);
                    continue;
                }

                var warnings = new List<string>();
                var draft = this.builder.Build(reminder.Path, reminder.Document, reminder.Hash, warnings);
                try
                {
                    listId ??= await this.gateway.GetOrCreateListAsync(this.options.ListName, cancellationToken).ConfigureAwait(false);
                    if (await this.WriteAsync(record, draft, listId, cancellationToken).ConfigureAwait(false))
                    {
                        report.Created++;
                    }
                    else
                    {
                        report.Updated++;
                    }

                    record.Hash = reminder.Hash;
                    record.State = RecordState.Synced;
                    record.LastSyncedUtc = now;
                    record.Message = string.Join(" ", warnings);
                    foreach (var warning in warnings)
                    {
                        this.LogWarning(reminder.Path, warning);
                    }
                }
                catch (HttpRequestException ex)
                {
                    // The hash is left at its last synced value, so the next run tries again.
                    record.State = RecordState.Error;
                    record.Message = ex.Message;
                    report.Failed++;
                    this.LogWriteFailed(reminder.Path, ex);
                }
            }

            foreach (var record in vanished)
            {
                if (record.State != RecordState.Missing)
                {
                    record.State = RecordState.Missing;
                    record.Message = "The file was not found on the last scan; its task was left as it was.";
                    this.LogMissing(record.Path);
                }

                report.Missing++;
            }
        }
        finally
        {
            // Saved even when a run is cut short, so a task created before the failure is never created twice.
            if (!dryRun)
            {
                this.store.Save(records.Values);
            }
        }

        return report;
    }

    /// <summary>
    /// Finds the record for a scanned file. A file with no record whose name matches a vanished
    /// record under the same root is the same reminder moved, since the reminders-set contract makes the
    /// name the identity and the directory the status.
    /// </summary>
    private ReminderRecord Resolve(
        Dictionary<string, ReminderRecord> records,
        List<ReminderRecord> vanished,
        ScannedReminder reminder,
        SyncReport report)
    {
        if (records.TryGetValue(reminder.Path, out var existing))
        {
            return existing;
        }

        var name = Path.GetFileName(reminder.Path);
        var moved = vanished.FirstOrDefault(record =>
            record.TaskId.Length > 0
            && PathComparer.Default.Equals(record.Root, reminder.Root)
            && PathComparer.Default.Equals(Path.GetFileName(record.Path), name));
        if (moved is not null)
        {
            vanished.Remove(moved);
            records.Remove(moved.Path);
            this.LogMoved(moved.Path, reminder.Path);
            moved.Path = reminder.Path;
            records[reminder.Path] = moved;
            report.Moved++;

            // The body carries the path, so a move is always a change worth writing.
            moved.Hash = string.Empty;
            return moved;
        }

        var created = new ReminderRecord { Path = reminder.Path, State = RecordState.Pending };
        records[reminder.Path] = created;
        return created;
    }

    /// <summary>
    /// Writes the task, recreating it when it was deleted in To Do.
    /// </summary>
    /// <returns><c>true</c> when a task was created; <c>false</c> when an existing one was updated.</returns>
    private async Task<bool> WriteAsync(ReminderRecord record, TodoTaskDraft draft, string listId, CancellationToken cancellationToken)
    {
        if (record.TaskId.Length > 0)
        {
            var taskList = record.ListId.Length > 0 ? record.ListId : listId;
            if (await this.gateway.UpdateTaskAsync(taskList, record.TaskId, draft, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            this.LogRecreating(record.Path);
        }

        record.TaskId = await this.gateway.CreateTaskAsync(listId, draft, cancellationToken).ConfigureAwait(false);
        record.ListId = listId;
        return true;
    }

    private void CopyFields(ReminderRecord record, ReminderDocument document)
    {
        var front = document.FrontMatter;
        record.Title = document.Title;
        record.Status = front.Get("status") ?? string.Empty;
        record.Category = front.Get("category") ?? string.Empty;
        record.Audience = front.Get("audience") ?? string.Empty;
        record.Subject = front.Get("subject") ?? string.Empty;
        record.Date = front.Get("date") ?? string.Empty;
        record.Plan = front.Get("plan") ?? string.Empty;
        record.Remind = front.Get(this.options.ReminderField) ?? string.Empty;
        record.Due = front.Get(this.options.DueField) ?? string.Empty;
    }

    [LoggerMessage(LogLevel.Warning, "{Path} is not a valid reminder: {Reason}")]
    private partial void LogInvalid(string path, string reason);

    [LoggerMessage(LogLevel.Information, "Dry run: would {Action} the task for {Path}")]
    private partial void LogWouldWrite(string action, string path);

    [LoggerMessage(LogLevel.Warning, "{Path}: {Warning}")]
    private partial void LogWarning(string path, string warning);

    [LoggerMessage(LogLevel.Error, "Could not write the task for {Path}")]
    private partial void LogWriteFailed(string path, Exception exception);

    [LoggerMessage(LogLevel.Information, "{Path} is gone; its task is left as it was")]
    private partial void LogMissing(string path);

    [LoggerMessage(LogLevel.Information, "{OldPath} moved to {NewPath}; keeping its task")]
    private partial void LogMoved(string oldPath, string newPath);

    [LoggerMessage(LogLevel.Information, "The task for {Path} was deleted in To Do; creating it again")]
    private partial void LogRecreating(string path);
}
