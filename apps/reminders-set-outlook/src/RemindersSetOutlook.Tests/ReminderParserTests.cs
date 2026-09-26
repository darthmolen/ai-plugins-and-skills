// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using RemindersSetOutlook.Scanning;
using Xunit;

namespace RemindersSetOutlook.Tests;

public sealed class ReminderParserTests
{
    private const string Reminder = "---\r\nkind: reminder\r\nstatus: open\r\ncategory: verify\r\naudience: developer\r\nsubject: display\r\ndate: 2026-09-01\r\nplan: screen-legibility_2026-08-27\r\nremind: 2026-10-01 09:30\r\n---\r\n\r\n# Check the screens on the low-end laptop\r\n\r\n## What to do\r\n\r\nOpen every screen at 1366x768.\r\n";

    [Fact]
    public void Parse_reads_front_matter_in_order_title_and_body()
    {
        var document = ReminderParser.Parse("/r/verify_screens_2026-09-01.md", Reminder);

        Assert.Equal("Check the screens on the low-end laptop", document.Title);
        Assert.Equal("reminder", document.Kind);
        Assert.Equal("open", document.Status);
        Assert.Equal("2026-10-01 09:30", document.FrontMatter.Get("REMIND"));
        Assert.Equal("kind", document.FrontMatter.Entries[0].Key);
        Assert.Equal("remind", document.FrontMatter.Entries[^1].Key);
        Assert.StartsWith("## What to do", document.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("# Check", document.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_without_front_matter_uses_file_name_when_there_is_no_heading()
    {
        var document = ReminderParser.Parse("/r/follow-up_thing_2026-09-01.md", "just text");

        Assert.Equal("follow-up_thing_2026-09-01", document.Title);
        Assert.Null(document.Kind);
        Assert.Equal("just text", document.Body);
    }

    [Fact]
    public void Parse_rejects_unterminated_front_matter()
    {
        Assert.Throws<FormatException>(() => ReminderParser.Parse("/r/x.md", "---\nkind: reminder\n# Title\n"));
    }

    [Fact]
    public void Parse_rejects_front_matter_that_is_not_yaml()
    {
        Assert.Throws<FormatException>(() => ReminderParser.Parse("/r/x.md", "---\nkind: [reminder\n---\n# Title\n"));
    }

    [Fact]
    public void Parse_flattens_lists()
    {
        var document = ReminderParser.Parse("/r/x.md", "---\nkind: reminder\ntags: [a, b]\n---\n");

        Assert.Equal("a, b", document.FrontMatter.Get("tags"));
    }
}
