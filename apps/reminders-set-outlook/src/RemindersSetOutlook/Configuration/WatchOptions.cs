// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

namespace RemindersSetOutlook.Configuration;

/// <summary>
/// Settings for the long-running <c>watch</c> command, bound from the <c>Watch</c> configuration section.
/// </summary>
internal sealed class WatchOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Watch";

    /// <summary>
    /// Gets or sets the number of minutes between scans.
    /// </summary>
    public int IntervalMinutes { get; set; } = 15;
}
