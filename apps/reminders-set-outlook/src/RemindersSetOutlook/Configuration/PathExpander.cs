// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.IO;

namespace RemindersSetOutlook.Configuration;

/// <summary>
/// Expands environment variables and a leading <c>~</c> in configured paths.
/// </summary>
internal static class PathExpander
{
    /// <summary>
    /// Expands and fully qualifies a configured path.
    /// </summary>
    /// <param name="path">The path as written in configuration.</param>
    /// <returns>The absolute path.</returns>
    public static string Expand(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (expanded == "~" || expanded.StartsWith("~/", StringComparison.Ordinal) || expanded.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expanded = home + expanded[1..];
        }

        return Path.GetFullPath(expanded);
    }
}
