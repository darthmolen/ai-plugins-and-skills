// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.IO;
using Microsoft.Extensions.Options;

namespace RemindersSetOutlook.Configuration;

/// <summary>
/// Resolves the files the application keeps on the local machine.
/// </summary>
internal sealed class AppPaths
{
    /// <summary>
    /// The folder name used under the per-user application data folders.
    /// </summary>
    public const string AppFolderName = "reminders-set-outlook";

    /// <summary>
    /// Initializes a new instance of the <see cref="AppPaths"/> class.
    /// </summary>
    /// <param name="options">The state options.</param>
    public AppPaths(IOptions<StateOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.Value.Directory;
        this.StateDirectory = string.IsNullOrWhiteSpace(configured)
            ? DefaultStateDirectory
            : PathExpander.Expand(configured);
        this.CsvFile = Path.Combine(this.StateDirectory, options.Value.CsvFileName);
        this.AuthRecordFile = Path.Combine(this.StateDirectory, "auth-record.json");
        this.LockFile = Path.Combine(this.StateDirectory, "sync.lock");
        this.LogDirectory = Path.Combine(this.StateDirectory, "logs");
    }

    /// <summary>
    /// Gets the per-user configuration file, layered over the <c>appsettings.json</c> shipped beside the executable.
    /// </summary>
    public static string UserConfigFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName,
        "appsettings.json");

    /// <summary>
    /// Gets the default state directory.
    /// </summary>
    public static string DefaultStateDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    /// <summary>
    /// Gets the state directory.
    /// </summary>
    public string StateDirectory { get; }

    /// <summary>
    /// Gets the findings CSV.
    /// </summary>
    public string CsvFile { get; }

    /// <summary>
    /// Gets the file holding the non-secret record of which account signed in.
    /// </summary>
    public string AuthRecordFile { get; }

    /// <summary>
    /// Gets the lock file that keeps two syncs from running at once.
    /// </summary>
    public string LockFile { get; }

    /// <summary>
    /// Gets the directory for rolling log files.
    /// </summary>
    public string LogDirectory { get; }
}
