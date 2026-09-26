// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace RemindersSetOutlook.Scanning;

/// <summary>
/// The YAML front matter of a reminder, flattened to ordered key/value text.
/// </summary>
internal sealed class FrontMatter
{
    private readonly List<KeyValuePair<string, string>> entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrontMatter"/> class.
    /// </summary>
    /// <param name="entries">The entries in document order.</param>
    public FrontMatter(IEnumerable<KeyValuePair<string, string>> entries)
    {
        this.entries = entries.ToList();
    }

    /// <summary>
    /// Gets front matter with no entries.
    /// </summary>
    public static FrontMatter Empty { get; } = new FrontMatter(Array.Empty<KeyValuePair<string, string>>());

    /// <summary>
    /// Gets the entries in document order.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Entries => this.entries;

    /// <summary>
    /// Gets the value of a key, compared case-insensitively.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The trimmed value, or <c>null</c> when the key is absent or empty.</returns>
    public string? Get(string key)
    {
        foreach (var entry in this.entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                var value = entry.Value.Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }
}
