// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using RemindersSetOutlook.Todo;
using Xunit;

namespace RemindersSetOutlook.Tests;

public sealed class TodoTaskPayloadTests
{
    [Fact]
    public void Build_writes_reminder_and_due_date()
    {
        var draft = new TodoTaskDraft(
            "Title",
            "Body",
            new[] { "verify" },
            false,
            new DateTime(2026, 10, 1, 13, 30, 0, DateTimeKind.Utc),
            new DateOnly(2026, 10, 15),
            "Eastern Standard Time");

        var json = TodoTaskPayload.Build(draft).ToJsonString();

        Assert.Contains("\"isReminderOn\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"reminderDateTime\":{\"dateTime\":\"2026-10-01T13:30:00\",\"timeZone\":\"UTC\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"dueDateTime\":{\"dateTime\":\"2026-10-15T00:00:00\",\"timeZone\":\"Eastern Standard Time\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"notStarted\"", json, StringComparison.Ordinal);
        Assert.Contains("\"categories\":[\"verify\"]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_clears_fields_that_were_removed_from_the_file()
    {
        var draft = new TodoTaskDraft("Title", "Body", Array.Empty<string>(), true, null, null, "UTC");

        var json = TodoTaskPayload.Build(draft).ToJsonString();

        Assert.Contains("\"isReminderOn\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"reminderDateTime\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"dueDateTime\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"completed\"", json, StringComparison.Ordinal);
    }
}
