// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RemindersSetOutlook.Configuration;
using RemindersSetOutlook.Scanning;
using Xunit;

namespace RemindersSetOutlook.Tests;

public sealed class ReminderScannerTests
{
    [Fact]
    public void Scan_finds_reminders_across_repositories_and_skips_everything_else()
    {
        using var temp = new TempDirectory();
        temp.Write("repo-a/planning/reminders/verify_a_2026-09-01.md", "---\nkind: reminder\nstatus: open\n---\n# A\n");
        temp.Write("repo-a/planning/reminders/completed/verify_b_2026-09-01.md", "---\nkind: reminder\nstatus: done\n---\n# B\n");
        temp.Write("repo-b/planning/reminders/decision_c_2026-09-01.md", "---\nkind: reminder\n---\n# C\n");
        temp.Write("repo-b/planning/reminders/README.md", "# Not a reminder\n");
        temp.Write("repo-b/planning/reminders/broken_2026-09-01.md", "---\nkind: reminder\n");
        temp.Write("repo-b/node_modules/pkg/planning/reminders/verify_d_2026-09-01.md", "---\nkind: reminder\n---\n# D\n");
        temp.Write("repo-b/docs/verify_e_2026-09-01.md", "---\nkind: reminder\n---\n# E\n");

        var options = new ScanOptions();
        options.Roots.Add(temp.Path);
        var scanner = new ReminderScanner(Options.Create(options), NullLogger<ReminderScanner>.Instance);

        var found = scanner.Scan();

        Assert.Equal(
            new[] { "broken_2026-09-01.md", "verify_b_2026-09-01.md", "verify_a_2026-09-01.md", "decision_c_2026-09-01.md" }.OrderBy(name => name),
            found.Select(reminder => System.IO.Path.GetFileName(reminder.Path)).OrderBy(name => name));
        var broken = found.Single(reminder => reminder.Path.EndsWith("broken_2026-09-01.md", StringComparison.Ordinal));
        Assert.Null(broken.Document);
        Assert.NotNull(broken.Error);
        Assert.All(found, reminder => Assert.Equal(temp.Path, reminder.Root));
    }

    [Fact]
    public void Scan_without_roots_explains_what_to_configure()
    {
        var scanner = new ReminderScanner(Options.Create(new ScanOptions()), NullLogger<ReminderScanner>.Instance);

        var error = Assert.Throws<InvalidOperationException>(() => scanner.Scan());
        Assert.Contains("Scan:Roots", error.Message, StringComparison.Ordinal);
    }
}
