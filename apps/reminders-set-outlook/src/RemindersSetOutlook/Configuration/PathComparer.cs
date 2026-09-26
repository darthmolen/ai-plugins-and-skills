// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;

namespace RemindersSetOutlook.Configuration;

/// <summary>
/// The string comparer that matches how the current operating system compares file paths.
/// </summary>
internal static class PathComparer
{
    /// <summary>
    /// Gets the comparer: case-insensitive on Windows and macOS, case-sensitive elsewhere.
    /// </summary>
    public static StringComparer Default { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
