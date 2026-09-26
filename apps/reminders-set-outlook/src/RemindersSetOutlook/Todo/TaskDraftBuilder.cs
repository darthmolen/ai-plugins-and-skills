// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Options;
using RemindersSetOutlook.Configuration;
using RemindersSetOutlook.Scanning;

namespace RemindersSetOutlook.Todo;

/// <summary>
/// Turns a reminder file into the To Do task that mirrors it.
/// </summary>
internal sealed class TaskDraftBuilder
{
    private static readonly string[] CompletedStatuses = { "done", "dropped" };

    private static readonly string[] CategoryFields = { "category", "audience", "subject" };

    private static readonly string[] DateTimeFormats =
    {
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm",
        "yyyy-MM-ddTHH:mm:ss",
    };

    private readonly TodoOptions options;
    private readonly TimeZoneInfo timeZone;
    private readonly TimeOnly defaultReminderTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskDraftBuilder"/> class.
    /// </summary>
    /// <param name="options">The To Do options.</param>
    /// <exception cref="InvalidOperationException">The configured time zone or default reminder time is invalid.</exception>
    public TaskDraftBuilder(IOptions<TodoOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        this.timeZone = ResolveTimeZone(this.options.TimeZone);
        if (!TimeOnly.TryParseExact(this.options.DefaultReminderTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out this.defaultReminderTime))
        {
            throw new InvalidOperationException(
                $"Todo:DefaultReminderTime '{this.options.DefaultReminderTime}' is not a time in HH:mm form.");
        }
    }

    /// <summary>
    /// Builds the task for a reminder.
    /// </summary>
    /// <param name="path">The reminder file path.</param>
    /// <param name="document">The parsed reminder.</param>
    /// <param name="hash">The content hash.</param>
    /// <param name="warnings">Receives problems that did not stop the task being built, such as an unreadable date.</param>
    /// <returns>The task draft.</returns>
    public TodoTaskDraft Build(string path, ReminderDocument document, string hash, ICollection<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(warnings);

        var status = document.Status ?? string.Empty;
        var completed = CompletedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);

        var reminderUtc = this.ParseReminder(document.FrontMatter.Get(this.options.ReminderField), warnings);
        var due = ParseDue(this.options.DueField, document.FrontMatter.Get(this.options.DueField), warnings);

        var categories = this.options.ApplyCategories
            ? CategoryFields
                .Select(key => document.FrontMatter.Get(key))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        return new TodoTaskDraft(
            document.Title,
            BuildBody(path, document, hash),
            categories,
            completed,
            completed ? null : reminderUtc,
            due,
            this.timeZone.Id);
    }

    /// <summary>
    /// Builds the plain-text task body: where the file is, its front matter, then its description.
    /// </summary>
    /// <param name="path">The reminder file path.</param>
    /// <param name="document">The parsed reminder.</param>
    /// <param name="hash">The content hash.</param>
    /// <returns>The body text.</returns>
    internal static string BuildBody(string path, ReminderDocument document, string hash)
    {
        var body = new StringBuilder();
        body.Append("File: ").AppendLine(path);
        body.AppendLine();

        if (document.FrontMatter.Entries.Count > 0)
        {
            foreach (var (key, value) in document.FrontMatter.Entries)
            {
                body.Append(key).Append(": ").AppendLine(value);
            }

            body.AppendLine();
        }

        if (document.Body.Length > 0)
        {
            body.AppendLine(document.Body);
            body.AppendLine();
        }

        body.Append("-- synced by reminders-set-outlook, content ").Append(hash[..Math.Min(12, hash.Length)]);
        return body.ToString();
    }

    private static DateOnly? ParseDue(string field, string? value, ICollection<string> warnings)
    {
        if (value is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due))
        {
            return due;
        }

        warnings.Add($"{field}: '{value}' is not a date in yyyy-MM-dd form; no due date was set.");
        return null;
    }

    private static TimeZoneInfo ResolveTimeZone(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(configured);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new InvalidOperationException($"Todo:TimeZone '{configured}' is not a known time zone id.", ex);
        }
    }

    private DateTime? ParseReminder(string? value, ICollection<string> warnings)
    {
        if (value is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            return this.ToUtc(dateOnly.ToDateTime(this.defaultReminderTime));
        }

        if (DateTime.TryParseExact(value, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return this.ToUtc(local);
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
        {
            return withOffset.UtcDateTime;
        }

        warnings.Add(
            $"{this.options.ReminderField}: '{value}' is not a date (yyyy-MM-dd) or date and time (yyyy-MM-dd HH:mm); no reminder was set.");
        return null;
    }

    private DateTime ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (this.timeZone.IsInvalidTime(unspecified))
        {
            // A wall-clock time skipped by a daylight saving jump; an hour later always exists.
            unspecified = unspecified.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, this.timeZone);
    }
}
