// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using RemindersSetOutlook.Configuration;
using RemindersSetOutlook.Scanning;
using RemindersSetOutlook.Todo;
using Xunit;

namespace RemindersSetOutlook.Tests;

public sealed class TaskDraftBuilderTests
{
    private const string Path = "/repo/planning/reminders/verify_screens_2026-09-01.md";

    [Fact]
    public void Build_sets_a_reminder_at_the_default_time_for_a_date()
    {
        var warnings = new List<string>();
        var draft = Builder("UTC").Build(Path, Parse("remind: 2026-10-01"), "abc", warnings);

        Assert.Equal(new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc), draft.ReminderUtc);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Build_reads_a_reminder_time_in_the_configured_time_zone()
    {
        var draft = Builder("America/New_York").Build(Path, Parse("remind: 2026-10-01 09:30"), "abc", new List<string>());

        // EDT is UTC-4 on 1 October.
        Assert.Equal(new DateTime(2026, 10, 1, 13, 30, 0, DateTimeKind.Utc), draft.ReminderUtc);
    }

    [Fact]
    public void Build_honours_an_explicit_offset()
    {
        var draft = Builder("UTC").Build(Path, Parse("remind: 2026-10-01T09:30:00+02:00"), "abc", new List<string>());

        Assert.Equal(new DateTime(2026, 10, 1, 7, 30, 0, DateTimeKind.Utc), draft.ReminderUtc);
    }

    [Fact]
    public void Build_without_a_reminder_field_sets_no_reminder()
    {
        var draft = Builder("UTC").Build(Path, Parse(), "abc", new List<string>());

        Assert.Null(draft.ReminderUtc);
        Assert.False(draft.Completed);
    }

    [Fact]
    public void Build_warns_and_skips_an_unreadable_reminder()
    {
        var warnings = new List<string>();
        var draft = Builder("UTC").Build(Path, Parse("remind: next tuesday"), "abc", warnings);

        Assert.Null(draft.ReminderUtc);
        Assert.Single(warnings);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("dropped")]
    public void Build_completes_closed_reminders_and_silences_their_alert(string status)
    {
        var draft = Builder("UTC").Build(Path, Parse("remind: 2026-10-01", status), "abc", new List<string>());

        Assert.True(draft.Completed);
        Assert.Null(draft.ReminderUtc);
    }

    [Fact]
    public void Build_sets_due_date_and_categories()
    {
        var draft = Builder("UTC").Build(Path, Parse("due: 2026-10-15"), "abc", new List<string>());

        Assert.Equal(new DateOnly(2026, 10, 15), draft.Due);
        Assert.Equal(new[] { "verify", "developer", "display" }, draft.Categories);
    }

    [Fact]
    public void Build_body_carries_path_front_matter_and_description()
    {
        var draft = Builder("UTC").Build(Path, Parse(), "0123456789abcdef", new List<string>());

        Assert.Equal("Check the screens", draft.Title);
        Assert.StartsWith("File: " + Path, draft.Body, StringComparison.Ordinal);
        Assert.Contains("audience: developer", draft.Body, StringComparison.Ordinal);
        Assert.Contains("Open every screen.", draft.Body, StringComparison.Ordinal);
        Assert.EndsWith("0123456789ab", draft.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_rejects_an_unknown_time_zone()
    {
        Assert.Throws<InvalidOperationException>(() => Builder("Not/AZone"));
    }

    private static TaskDraftBuilder Builder(string timeZone)
    {
        return new TaskDraftBuilder(Options.Create(new TodoOptions { TimeZone = timeZone }));
    }

    private static ReminderDocument Parse(string extra = "", string status = "open")
    {
        var content = $"---\nkind: reminder\nstatus: {status}\ncategory: verify\naudience: developer\nsubject: display\n{extra}\n---\n\n# Check the screens\n\nOpen every screen.\n";
        return ReminderParser.Parse(Path, content);
    }
}
