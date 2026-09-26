// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace RemindersSetOutlook.Scanning;

/// <summary>
/// Splits a reminder file into front matter, title and body.
/// </summary>
internal static class ReminderParser
{
    private const string Fence = "---";

    /// <summary>
    /// Parses a reminder file.
    /// </summary>
    /// <param name="path">The file path, used for the fallback title.</param>
    /// <param name="content">The file content.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="FormatException">The front matter is unterminated or is not a YAML mapping.</exception>
    public static ReminderDocument Parse(string path, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var frontMatter = FrontMatter.Empty;
        var bodyStart = 0;
        if (lines.Length > 0 && lines[0].TrimEnd() == Fence)
        {
            var close = Array.FindIndex(lines, 1, line => line.TrimEnd() == Fence);
            if (close < 0)
            {
                throw new FormatException("The front matter opens with '---' but never closes.");
            }

            frontMatter = ParseYaml(string.Join('\n', lines[1..close]));
            bodyStart = close + 1;
        }

        var bodyLines = lines[bodyStart..].ToList();
        var title = Path.GetFileNameWithoutExtension(path);
        var headingIndex = bodyLines.FindIndex(line => line.StartsWith("# ", StringComparison.Ordinal));
        if (headingIndex >= 0)
        {
            title = bodyLines[headingIndex][2..].Trim();
            bodyLines.RemoveAt(headingIndex);
        }

        return new ReminderDocument(title, frontMatter, string.Join('\n', bodyLines).Trim());
    }

    private static FrontMatter ParseYaml(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return FrontMatter.Empty;
        }

        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(yaml);
            stream.Load(reader);
        }
        catch (YamlException ex)
        {
            throw new FormatException($"The front matter is not valid YAML: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0)
        {
            return FrontMatter.Empty;
        }

        if (stream.Documents[0].RootNode is not YamlMappingNode mapping)
        {
            throw new FormatException("The front matter is not a YAML mapping.");
        }

        var entries = new List<KeyValuePair<string, string>>();
        foreach (var (key, value) in mapping.Children)
        {
            entries.Add(new KeyValuePair<string, string>(Render(key), Render(value)));
        }

        return new FrontMatter(entries);
    }

    private static string Render(YamlNode node)
    {
        return node switch
        {
            YamlScalarNode scalar => scalar.Value ?? string.Empty,
            YamlSequenceNode sequence => string.Join(", ", sequence.Children.Select(Render)),
            YamlMappingNode mapping => string.Join("; ", mapping.Children.Select(child => $"{Render(child.Key)}: {Render(child.Value)}")),
            _ => node.ToString(),
        };
    }
}
