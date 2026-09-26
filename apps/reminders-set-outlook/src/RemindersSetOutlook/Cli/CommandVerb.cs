// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

namespace RemindersSetOutlook.Cli;

/// <summary>
/// The commands the executable understands.
/// </summary>
internal enum CommandVerb
{
    /// <summary>
    /// Scan once and write tasks.
    /// </summary>
    Sync,

    /// <summary>
    /// Scan on an interval until stopped.
    /// </summary>
    Watch,

    /// <summary>
    /// Sign in interactively and save the account.
    /// </summary>
    Login,

    /// <summary>
    /// Forget the saved account.
    /// </summary>
    Logout,

    /// <summary>
    /// Print the findings CSV summary.
    /// </summary>
    Status,

    /// <summary>
    /// Print usage.
    /// </summary>
    Help,
}
