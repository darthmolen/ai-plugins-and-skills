// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Security.Cryptography;
using System.Text;

namespace RemindersSetOutlook.Scanning;

/// <summary>
/// Hashes reminder content so that a change, and only a change, triggers a task update.
/// </summary>
internal static class ContentHasher
{
    /// <summary>
    /// Computes the SHA-256 of the content with line endings normalized to LF, so a CRLF checkout
    /// and an LF checkout of the same commit hash the same.
    /// </summary>
    /// <param name="content">The file content.</param>
    /// <returns>The lowercase hexadecimal hash.</returns>
    public static string Compute(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash);
    }
}
