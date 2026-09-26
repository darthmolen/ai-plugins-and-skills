// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using RemindersSetOutlook.Configuration;

namespace RemindersSetOutlook.State;

/// <summary>
/// Reads and writes the findings CSV.
/// </summary>
internal sealed class ReminderStateStore
{
    private static readonly CsvConfiguration CsvConfiguration = new(CultureInfo.InvariantCulture)
    {
        MissingFieldFound = null,
        HeaderValidated = null,
    };

    private readonly string csvFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReminderStateStore"/> class.
    /// </summary>
    /// <param name="paths">The application paths.</param>
    public ReminderStateStore(AppPaths paths)
        : this(paths?.CsvFile ?? throw new ArgumentNullException(nameof(paths)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReminderStateStore"/> class.
    /// </summary>
    /// <param name="csvFile">The CSV file path.</param>
    public ReminderStateStore(string csvFile)
    {
        this.csvFile = csvFile;
    }

    /// <summary>
    /// Gets the CSV file path.
    /// </summary>
    public string CsvFile => this.csvFile;

    /// <summary>
    /// Loads every record, keyed by path.
    /// </summary>
    /// <returns>The records; empty when the CSV does not exist yet.</returns>
    public Dictionary<string, ReminderRecord> Load()
    {
        var records = new Dictionary<string, ReminderRecord>(PathComparer.Default);
        if (!File.Exists(this.csvFile))
        {
            return records;
        }

        using var reader = new StreamReader(this.csvFile, Encoding.UTF8);
        using var csv = new CsvReader(reader, CsvConfiguration);
        csv.Context.RegisterClassMap<ReminderRecordMap>();
        foreach (var record in csv.GetRecords<ReminderRecord>())
        {
            records[record.Path] = record;
        }

        return records;
    }

    /// <summary>
    /// Replaces the CSV with the given records, atomically, ordered by path.
    /// </summary>
    /// <param name="records">The records to write.</param>
    public void Save(IEnumerable<ReminderRecord> records)
    {
        var directory = Path.GetDirectoryName(this.csvFile);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = this.csvFile + ".tmp";
        using (var writer = new StreamWriter(temp, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        using (var csv = new CsvWriter(writer, CsvConfiguration))
        {
            csv.Context.RegisterClassMap<ReminderRecordMap>();
            csv.WriteRecords(records.OrderBy(record => record.Path, PathComparer.Default));
        }

        File.Move(temp, this.csvFile, overwrite: true);
    }
}
