// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using RemindersSetOutlook.Cli;
using Xunit;

namespace RemindersSetOutlook.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void Parse_defaults_to_sync()
    {
        var command = CommandLine.Parse(Array.Empty<string>());

        Assert.Equal(CommandVerb.Sync, command.Verb);
        Assert.False(command.DryRun);
    }

    [Fact]
    public void Parse_reads_verb_flags_and_configuration_overrides()
    {
        var command = CommandLine.Parse(new[] { "watch", "--dry-run", "--force", "--Todo:ListName=Work", "--Scan:Roots:0", "/src" });

        Assert.Equal(CommandVerb.Watch, command.Verb);
        Assert.True(command.DryRun);
        Assert.True(command.Force);
        Assert.Equal(new[] { "--Todo:ListName=Work", "--Scan:Roots:0", "/src" }, command.ConfigurationArgs);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("--nope")]
    public void Parse_rejects_what_it_does_not_understand(string arg)
    {
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(new[] { arg }));
    }
}
