// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace RemindersSetOutlook.Todo;

/// <summary>
/// Serializes a <see cref="TodoTaskDraft"/> into a Microsoft Graph <c>todoTask</c> body.
/// </summary>
internal static class TodoTaskPayload
{
    private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>
    /// Builds the JSON body for a create (POST) or update (PATCH).
    /// </summary>
    /// <param name="draft">The task draft.</param>
    /// <returns>The JSON object.</returns>
    public static JsonObject Build(TodoTaskDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var payload = new JsonObject
        {
            ["title"] = draft.Title,
            ["body"] = new JsonObject
            {
                ["contentType"] = "text",
                ["content"] = draft.Body,
            },
            ["categories"] = new JsonArray(draft.Categories.Select(category => (JsonNode?)JsonValue.Create(category)).ToArray()),
            ["status"] = draft.Completed ? "completed" : "notStarted",
            ["isReminderOn"] = draft.ReminderUtc is not null,
            ["reminderDateTime"] = draft.ReminderUtc is { } reminder
                ? DateTimeTimeZone(reminder.ToString(DateTimeFormat, CultureInfo.InvariantCulture), "UTC")
                : null,
            ["dueDateTime"] = draft.Due is { } due
                ? DateTimeTimeZone(due.ToDateTime(TimeOnly.MinValue).ToString(DateTimeFormat, CultureInfo.InvariantCulture), draft.TimeZoneId)
                : null,
        };

        return payload;
    }

    private static JsonObject DateTimeTimeZone(string dateTime, string timeZone)
    {
        return new JsonObject
        {
            ["dateTime"] = dateTime,
            ["timeZone"] = timeZone,
        };
    }
}
