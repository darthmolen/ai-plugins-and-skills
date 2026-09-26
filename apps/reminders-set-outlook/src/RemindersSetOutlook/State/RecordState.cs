// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

namespace RemindersSetOutlook.State;

/// <summary>
/// Where a finding stands relative to its To Do task.
/// </summary>
internal enum RecordState
{
    /// <summary>
    /// Found, but no task has been written yet (a dry run, or the first sync has not happened).
    /// </summary>
    Pending,

    /// <summary>
    /// The task matches the file's current hash.
    /// </summary>
    Synced,

    /// <summary>
    /// The file could not be parsed; its task, if any, is left alone until it is fixed.
    /// </summary>
    Invalid,

    /// <summary>
    /// The last attempt to write the task failed; it is retried on the next sync.
    /// </summary>
    Error,

    /// <summary>
    /// The file was not found on the last scan. The task is left alone, because a reminder is never deleted.
    /// </summary>
    Missing,
}
