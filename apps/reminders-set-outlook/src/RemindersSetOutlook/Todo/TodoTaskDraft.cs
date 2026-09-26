// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Collections.Generic;

namespace RemindersSetOutlook.Todo;

/// <summary>
/// The full desired state of a To Do task. Every write sends all of it, so a field removed from
/// the file is cleared on the task rather than left behind.
/// </summary>
/// <param name="Title">The task title.</param>
/// <param name="Body">The plain-text task body.</param>
/// <param name="Categories">The task categories.</param>
/// <param name="Completed">Whether the task is completed.</param>
/// <param name="ReminderUtc">When the reminder alert fires, or <c>null</c> for no alert.</param>
/// <param name="Due">The due date, or <c>null</c> for none.</param>
/// <param name="TimeZoneId">The time zone the due date is expressed in.</param>
internal sealed record TodoTaskDraft(
    string Title,
    string Body,
    IReadOnlyList<string> Categories,
    bool Completed,
    DateTime? ReminderUtc,
    DateOnly? Due,
    string TimeZoneId);
