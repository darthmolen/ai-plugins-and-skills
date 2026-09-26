// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemindersSetOutlook.Configuration;

namespace RemindersSetOutlook.Scanning;

/// <summary>
/// Walks the configured folder hierarchy and returns every reminder file in it.
/// </summary>
internal sealed partial class ReminderScanner
{
    private readonly ScanOptions options;
    private readonly ILogger<ReminderScanner> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReminderScanner"/> class.
    /// </summary>
    /// <param name="options">The scan options.</param>
    /// <param name="logger">The logger.</param>
    public ReminderScanner(IOptions<ScanOptions> options, ILogger<ReminderScanner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        this.logger = logger;
    }

    /// <summary>
    /// Scans every configured root.
    /// </summary>
    /// <returns>The reminders found, ordered by path.</returns>
    /// <exception cref="InvalidOperationException">No roots are configured.</exception>
    public IReadOnlyList<ScannedReminder> Scan()
    {
        if (this.options.Roots.Count == 0)
        {
            throw new InvalidOperationException(
                "No folders to scan. Set Scan:Roots in appsettings.json (see the app README).");
        }

        var matcher = new Matcher(PathComparer.Default == StringComparer.Ordinal
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(this.options.Include.Count > 0 ? this.options.Include : ScanOptions.DefaultInclude);
        matcher.AddExcludePatterns(this.options.Exclude.Count > 0 ? this.options.Exclude : ScanOptions.DefaultExclude);

        var found = new Dictionary<string, ScannedReminder>(PathComparer.Default);
        foreach (var configuredRoot in this.options.Roots)
        {
            var root = PathExpander.Expand(configuredRoot);
            if (!Directory.Exists(root))
            {
                this.LogRootMissing(root);
                continue;
            }

            foreach (var path in matcher.GetResultsInFullPath(root))
            {
                var full = Path.GetFullPath(path);
                if (found.ContainsKey(full))
                {
                    continue;
                }

                var reminder = this.Read(root, full);
                if (reminder is not null)
                {
                    found[full] = reminder;
                }
            }
        }

        return found.Values.OrderBy(reminder => reminder.Path, PathComparer.Default).ToList();
    }

    private ScannedReminder? Read(string root, string path)
    {
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            this.LogReadFailed(path, ex);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            this.LogReadFailed(path, ex);
            return null;
        }

        var hash = ContentHasher.Compute(content);
        try
        {
            var document = ReminderParser.Parse(path, content);
            if (this.options.RequireReminderKind
                && !string.Equals(document.Kind, "reminder", StringComparison.OrdinalIgnoreCase))
            {
                this.LogNotAReminder(path);
                return null;
            }

            return new ScannedReminder(root, path, hash, document, null);
        }
        catch (FormatException ex)
        {
            return new ScannedReminder(root, path, hash, null, ex.Message);
        }
    }

    [LoggerMessage(LogLevel.Warning, "Scan root {Root} does not exist; skipping it")]
    private partial void LogRootMissing(string root);

    [LoggerMessage(LogLevel.Warning, "Could not read {Path}")]
    private partial void LogReadFailed(string path, Exception exception);

    [LoggerMessage(LogLevel.Debug, "{Path} has no 'kind: reminder' front matter; ignoring it")]
    private partial void LogNotAReminder(string path);
}
