// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

namespace RemindersSetOutlook.Configuration;

/// <summary>
/// Where local state lives, bound from the <c>State</c> configuration section.
/// </summary>
internal sealed class StateOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "State";

    /// <summary>
    /// Gets or sets the state directory. Empty means the per-user local application data folder.
    /// </summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file name of the findings CSV inside the state directory.
    /// </summary>
    public string CsvFileName { get; set; } = "reminders.csv";
}
