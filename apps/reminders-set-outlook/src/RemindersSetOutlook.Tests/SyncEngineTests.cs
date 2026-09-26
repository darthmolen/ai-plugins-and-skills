// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RemindersSetOutlook.Configuration;
using RemindersSetOutlook.Scanning;
using RemindersSetOutlook.State;
using RemindersSetOutlook.Sync;
using RemindersSetOutlook.Todo;
using Xunit;

namespace RemindersSetOutlook.Tests;

public sealed class SyncEngineTests : IDisposable
{
    private const string OpenReminder = "---\nkind: reminder\nstatus: open\ncategory: verify\n---\n\n# Look at the thing\n\nGo and look.\n";

    private readonly TempDirectory temp = new();
    private readonly FakeTodoGateway gateway = new();
    private readonly ReminderStateStore store;
    private readonly SyncEngine engine;

    public SyncEngineTests()
    {
        this.store = new ReminderStateStore(Path.Combine(this.temp.Path, "state", "reminders.csv"));
        var scan = new ScanOptions();
        scan.Roots.Add(Path.Combine(this.temp.Path, "repos"));
        var scanner = new ReminderScanner(Options.Create(scan), NullLogger<ReminderScanner>.Instance);
        var todo = Options.Create(new TodoOptions { TimeZone = "UTC" });
        this.engine = new SyncEngine(
            scanner,
            this.store,
            new TaskDraftBuilder(todo),
            this.gateway,
            todo,
            TimeProvider.System,
            NullLogger<SyncEngine>.Instance);
    }

    [Fact]
    public async Task A_new_reminder_gets_a_task_and_a_row()
    {
        var path = this.WriteReminder("planning/reminders/verify_thing_2026-09-01.md", OpenReminder);

        var report = await this.RunAsync();

        Assert.Equal(1, report.Created);
        var record = this.store.Load()[path];
        Assert.Equal(RecordState.Synced, record.State);
        Assert.Equal("task-1", record.TaskId);
        Assert.Equal("list-1", record.ListId);
        Assert.Equal(ContentHasher.Compute(OpenReminder), record.Hash);
        Assert.Equal("verify", record.Category);
        Assert.Equal("Look at the thing", this.gateway.Tasks["task-1"].Title);
    }

    [Fact]
    public async Task An_unchanged_reminder_is_not_written_again()
    {
        this.WriteReminder("planning/reminders/verify_thing_2026-09-01.md", OpenReminder);
        await this.RunAsync();
        this.gateway.Calls.Clear();

        var report = await this.RunAsync();

        Assert.Equal(1, report.Unchanged);
        Assert.Empty(this.gateway.Calls);
    }

    [Fact]
    public async Task A_changed_hash_updates_the_same_task()
    {
        var path = this.WriteReminder("planning/reminders/verify_thing_2026-09-01.md", OpenReminder);
        await this.RunAsync();

        File.WriteAllText(path, OpenReminder.Replace("status: open", "status: done\nclosed: 2026-09-20 - looked; fine", StringComparison.Ordinal));
        var report = await this.RunAsync();

        Assert.Equal(1, report.Updated);
        Assert.Contains("update task-1", this.gateway.Calls);
        Assert.True(this.gateway.Tasks["task-1"].Completed);
        Assert.Equal("done", this.store.Load()[path].Status);
    }

    [Fact]
    public async Task Filing_a_reminder_into_completed_keeps_its_task()
    {
        var original = this.WriteReminder("planning/reminders/verify_thing_2026-09-01.md", OpenReminder);
        await this.RunAsync();

        var moved = this.WriteReminder("planning/reminders/completed/verify_thing_2026-09-01.md", OpenReminder);
        File.Delete(original);
        var report = await this.RunAsync();

        Assert.Equal(1, report.Moved);
        Assert.Equal(0, report.Created);
        Assert.Single(this.gateway.Tasks);
        var records = this.store.Load();
        Assert.False(records.ContainsKey(original));
        Assert.Equal("task-1", records[moved].TaskId);
        Assert.Contains(moved, this.gateway.Tasks["task-1"].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deleted_file_is_marked_missing_and_its_task_left_alone()
    {
        var path = this.WriteReminder("planning/reminders/verify_thing_2026-09-01.md", OpenReminder);
        await this.RunAsync();
        this.gateway.Calls.Clear();

        File.Delete(path);
        var report = await this.RunAsync();

        Assert.Equal(1, report.Missing);
        Assert.Equal(RecordState.Missing, this.store.Load()[path].State);
        Assert.Empty(this.gateway.Calls);
    }

    [Fact]
    public async Task A_task_deleted_in_todo_is_created_again_on_the_next_change()
    {
        var path = this.WriteReminder("planning/reminders/verify_thing_2026-09-01.md", OpenReminder);
        await this.RunAsync();
        this.gateway.Tasks.Clear();

        File.AppendAllText(path, "\nMore detail.\n");
        var report = await this.RunAsync();

        Assert.Equal(1, report.Created);
        Assert.Equal("task-2", this.store.Load()[path].TaskId);
    }

    [Fact]
    public async Task A_failed_write_keeps_the_old_hash_and_is_retried()
    {
        var path = this.WriteReminder("planning/reminders/verify_thing_2026-09-01.md", OpenReminder);
        this.gateway.FailWrites = true;

        var failed = await this.RunAsync();

        Assert.Equal(1, failed.Failed);
        var record = this.store.Load()[path];
        Assert.Equal(RecordState.Error, record.State);
        Assert.Equal(string.Empty, record.Hash);
        Assert.Contains("boom", record.Message, StringComparison.Ordinal);

        this.gateway.FailWrites = false;
        var retried = await this.RunAsync();

        Assert.Equal(1, retried.Created);
        Assert.Equal(RecordState.Synced, this.store.Load()[path].State);
    }

    [Fact]
    public async Task An_invalid_file_is_recorded_but_never_sent()
    {
        var path = this.WriteReminder("planning/reminders/verify_thing_2026-09-01.md", "---\nkind: reminder\n# never closed\n");

        var report = await this.RunAsync();

        Assert.Equal(1, report.Invalid);
        Assert.Equal(RecordState.Invalid, this.store.Load()[path].State);
        Assert.Empty(this.gateway.Tasks);
    }

    [Fact]
    public async Task A_dry_run_writes_nothing()
    {
        this.WriteReminder("planning/reminders/verify_thing_2026-09-01.md", OpenReminder);

        var report = await this.engine.RunAsync(dryRun: true, force: false, TestContext.Current.CancellationToken);

        Assert.Equal(1, report.Created);
        Assert.Empty(this.gateway.Calls);
        Assert.False(File.Exists(this.store.CsvFile));
    }

    [Fact]
    public async Task Force_rewrites_unchanged_tasks()
    {
        this.WriteReminder("planning/reminders/verify_thing_2026-09-01.md", OpenReminder);
        await this.RunAsync();

        var report = await this.engine.RunAsync(dryRun: false, force: true, TestContext.Current.CancellationToken);

        Assert.Equal(1, report.Updated);
        Assert.Equal(1, this.gateway.Calls.Count(call => call.StartsWith("update", StringComparison.Ordinal)));
    }

    public void Dispose()
    {
        this.temp.Dispose();
    }

    private string WriteReminder(string relativePath, string content)
    {
        return this.temp.Write(Path.Combine("repos", "repo-a", relativePath), content);
    }

    private Task<SyncReport> RunAsync()
    {
        return this.engine.RunAsync(dryRun: false, force: false, TestContext.Current.CancellationToken);
    }
}
