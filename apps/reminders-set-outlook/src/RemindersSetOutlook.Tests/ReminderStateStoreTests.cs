// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.IO;
using RemindersSetOutlook.State;
using Xunit;

namespace RemindersSetOutlook.Tests;

public sealed class ReminderStateStoreTests
{
    [Fact]
    public void Load_returns_nothing_before_the_first_save()
    {
        using var temp = new TempDirectory();

        Assert.Empty(new ReminderStateStore(Path.Combine(temp.Path, "reminders.csv")).Load());
    }

    [Fact]
    public void Save_then_load_round_trips_every_column()
    {
        using var temp = new TempDirectory();
        var store = new ReminderStateStore(Path.Combine(temp.Path, "state", "reminders.csv"));
        var seen = new DateTimeOffset(2026, 9, 26, 8, 15, 0, TimeSpan.Zero);
        var record = new ReminderRecord
        {
            Path = "/repo/planning/reminders/verify_a, \"quoted\"_2026-09-01.md",
            Root = "/repo",
            State = RecordState.Synced,
            Hash = "abc",
            ListId = "list",
            TaskId = "task",
            Title = "Title, with comma",
            Status = "open",
            Remind = "2026-10-01 09:00",
            FirstSeenUtc = seen,
            LastSeenUtc = seen,
            LastSyncedUtc = seen,
            Message = "line one\nline two",
        };

        store.Save(new[] { record });
        var loaded = store.Load()[record.Path];

        Assert.Equal(record.Title, loaded.Title);
        Assert.Equal(RecordState.Synced, loaded.State);
        Assert.Equal(seen, loaded.LastSyncedUtc);
        Assert.Equal(record.Message, loaded.Message);
        Assert.Equal(string.Empty, loaded.Due);
    }
}
