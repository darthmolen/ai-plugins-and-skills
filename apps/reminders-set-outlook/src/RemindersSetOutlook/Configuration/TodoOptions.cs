// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

namespace RemindersSetOutlook.Configuration;

/// <summary>
/// How reminders become Microsoft To Do tasks, bound from the <c>Todo</c> configuration section.
/// </summary>
internal sealed class TodoOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Todo";

    /// <summary>
    /// Gets or sets the display name of the To Do list the tasks are written to. It is created if missing.
    /// </summary>
    public string ListName { get; set; } = "Reminders";

    /// <summary>
    /// Gets or sets the front matter key that asks for a reminder alert, for example <c>remind: 2026-10-01 09:00</c>.
    /// </summary>
    public string ReminderField { get; set; } = "remind";

    /// <summary>
    /// Gets or sets the front matter key that carries a due date, for example <c>due: 2026-10-01</c>.
    /// </summary>
    public string DueField { get; set; } = "due";

    /// <summary>
    /// Gets or sets the time of day used when the reminder field holds only a date, as <c>HH:mm</c>.
    /// </summary>
    public string DefaultReminderTime { get; set; } = "09:00";

    /// <summary>
    /// Gets or sets the time zone, Windows or IANA id, that reminder and due values without an offset are read in.
    /// Empty means the machine's local time zone.
    /// </summary>
    public string TimeZone { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the reminder's category, audience and subject become task categories.
    /// </summary>
    public bool ApplyCategories { get; set; } = true;
}
