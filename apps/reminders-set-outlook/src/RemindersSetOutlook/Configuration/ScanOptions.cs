// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System.Collections.Generic;

namespace RemindersSetOutlook.Configuration;

/// <summary>
/// Where to look for reminder files, bound from the <c>Scan</c> configuration section.
/// </summary>
internal sealed class ScanOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Scan";

    /// <summary>
    /// Gets the include patterns used when none are configured: the reminders-set skill's own directory.
    /// </summary>
    public static IReadOnlyList<string> DefaultInclude { get; } = new[] { "**/planning/reminders/**/*.md" };

    /// <summary>
    /// Gets the exclude patterns used when none are configured.
    /// </summary>
    public static IReadOnlyList<string> DefaultExclude { get; } = new[]
    {
        "**/.git/**",
        "**/node_modules/**",
        "**/bin/**",
        "**/obj/**",
    };

    /// <summary>
    /// Gets the folders to scan. Each is searched recursively with <see cref="Include"/> and <see cref="Exclude"/>.
    /// Environment variables and a leading <c>~</c> are expanded.
    /// </summary>
    public IList<string> Roots { get; } = new List<string>();

    /// <summary>
    /// Gets the glob patterns, relative to each root, that select candidate files.
    /// When empty, <see cref="DefaultInclude"/> is used.
    /// </summary>
    public IList<string> Include { get; } = new List<string>();

    /// <summary>
    /// Gets the glob patterns, relative to each root, that are never scanned.
    /// When empty, <see cref="DefaultExclude"/> is used.
    /// </summary>
    public IList<string> Exclude { get; } = new List<string>();

    /// <summary>
    /// Gets or sets a value indicating whether a file must carry <c>kind: reminder</c> in its front matter to count.
    /// </summary>
    public bool RequireReminderKind { get; set; } = true;
}
