// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

namespace RemindersSetOutlook.Sync;

/// <summary>
/// What one sync did.
/// </summary>
internal sealed class SyncReport
{
    /// <summary>
    /// Gets or sets the number of reminder files found.
    /// </summary>
    public int Found { get; set; }

    /// <summary>
    /// Gets or sets the number of tasks created.
    /// </summary>
    public int Created { get; set; }

    /// <summary>
    /// Gets or sets the number of tasks updated because their file's hash changed.
    /// </summary>
    public int Updated { get; set; }

    /// <summary>
    /// Gets or sets the number of files whose hash had not changed.
    /// </summary>
    public int Unchanged { get; set; }

    /// <summary>
    /// Gets or sets the number of files recognised as moved, for example into <c>completed/</c>.
    /// </summary>
    public int Moved { get; set; }

    /// <summary>
    /// Gets or sets the number of previously found files that have gone.
    /// </summary>
    public int Missing { get; set; }

    /// <summary>
    /// Gets or sets the number of files that could not be parsed.
    /// </summary>
    public int Invalid { get; set; }

    /// <summary>
    /// Gets or sets the number of task writes that failed.
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this was a dry run that wrote nothing.
    /// </summary>
    public bool DryRun { get; set; }

    /// <inheritdoc/>
    public override string ToString()
    {
        var verb = this.DryRun ? "would be " : string.Empty;
        return $"{this.Found} found: {this.Created} {verb}created, {this.Updated} {verb}updated, {this.Unchanged} unchanged, "
            + $"{this.Moved} moved, {this.Missing} missing, {this.Invalid} invalid, {this.Failed} failed";
    }
}
