// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

namespace RemindersSetOutlook.Scanning;

/// <summary>
/// A parsed reminder file.
/// </summary>
/// <param name="Title">The first level-one heading, or the file name when there is none.</param>
/// <param name="FrontMatter">The front matter.</param>
/// <param name="Body">The Markdown after the front matter, without the title heading.</param>
internal sealed record ReminderDocument(string Title, FrontMatter FrontMatter, string Body)
{
    /// <summary>
    /// Gets the <c>kind</c> field.
    /// </summary>
    public string? Kind => this.FrontMatter.Get("kind");

    /// <summary>
    /// Gets the <c>status</c> field.
    /// </summary>
    public string? Status => this.FrontMatter.Get("status");
}
