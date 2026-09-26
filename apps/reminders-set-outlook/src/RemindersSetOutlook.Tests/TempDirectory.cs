// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.IO;

namespace RemindersSetOutlook.Tests;

/// <summary>
/// A scratch directory deleted when the test ends.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rso-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.Path);
    }

    public string Path { get; }

    public string Write(string relativePath, string content)
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(this.Path, relativePath));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        Directory.Delete(this.Path, recursive: true);
    }
}
