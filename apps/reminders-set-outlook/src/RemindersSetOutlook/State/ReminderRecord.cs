// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;

namespace RemindersSetOutlook.State;

/// <summary>
/// One row of the findings CSV: a reminder file and the To Do task that mirrors it.
/// </summary>
internal sealed class ReminderRecord
{
    /// <summary>
    /// Gets or sets the absolute file path. This is the row's key.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the configured root the file was found under.
    /// </summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets where the finding stands.
    /// </summary>
    public RecordState State { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 of the file content that was last written to the task.
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the To Do list id.
    /// </summary>
    public string ListId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the To Do task id.
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the reminder title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the <c>status</c> front matter field.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the <c>category</c> front matter field.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the <c>audience</c> front matter field.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the <c>subject</c> front matter field.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the <c>date</c> front matter field.
    /// </summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the <c>plan</c> front matter field.
    /// </summary>
    public string Plan { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the reminder field, as written in the file.
    /// </summary>
    public string Remind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the due field, as written in the file.
    /// </summary>
    public string Due { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the file was first found.
    /// </summary>
    public DateTimeOffset? FirstSeenUtc { get; set; }

    /// <summary>
    /// Gets or sets when the file was last found.
    /// </summary>
    public DateTimeOffset? LastSeenUtc { get; set; }

    /// <summary>
    /// Gets or sets when the task was last written.
    /// </summary>
    public DateTimeOffset? LastSyncedUtc { get; set; }

    /// <summary>
    /// Gets or sets the last error or warning, empty when there is none.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
