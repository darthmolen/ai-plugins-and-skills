// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

namespace RemindersSetOutlook.Scanning;

/// <summary>
/// One reminder file found by a scan.
/// </summary>
/// <param name="Root">The configured root the file was found under.</param>
/// <param name="Path">The absolute file path.</param>
/// <param name="Hash">The content hash.</param>
/// <param name="Document">The parsed document, or <c>null</c> when parsing failed.</param>
/// <param name="Error">The parse error, or <c>null</c> when parsing succeeded.</param>
internal sealed record ScannedReminder(string Root, string Path, string Hash, ReminderDocument? Document, string? Error);
