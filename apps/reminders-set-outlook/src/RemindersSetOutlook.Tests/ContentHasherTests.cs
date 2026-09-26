// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using RemindersSetOutlook.Scanning;
using Xunit;

namespace RemindersSetOutlook.Tests;

public sealed class ContentHasherTests
{
    [Fact]
    public void Compute_ignores_line_ending_style()
    {
        Assert.Equal(ContentHasher.Compute("a\nb\n"), ContentHasher.Compute("a\r\nb\r\n"));
    }

    [Fact]
    public void Compute_changes_with_content()
    {
        Assert.NotEqual(ContentHasher.Compute("status: open"), ContentHasher.Compute("status: done"));
        Assert.Equal(64, ContentHasher.Compute(string.Empty).Length);
    }
}
