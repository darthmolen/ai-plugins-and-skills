// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.IO;

namespace RemindersSetOutlook.Sync;

/// <summary>
/// An exclusive lock file, so a scheduled run and a manual run cannot both write the CSV.
/// </summary>
internal sealed class SyncLock : IDisposable
{
    private readonly FileStream stream;

    private SyncLock(FileStream stream)
    {
        this.stream = stream;
    }

    /// <summary>
    /// Tries to take the lock.
    /// </summary>
    /// <param name="path">The lock file path.</param>
    /// <returns>The held lock, or <c>null</c> when another process holds it.</returns>
    public static SyncLock? TryAcquire(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            return new SyncLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.stream.Dispose();
    }
}
