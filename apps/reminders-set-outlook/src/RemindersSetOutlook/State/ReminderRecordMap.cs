// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System.Globalization;
using CsvHelper.Configuration;

namespace RemindersSetOutlook.State;

/// <summary>
/// Maps <see cref="ReminderRecord"/> to CSV columns, writing timestamps in round-trip form.
/// </summary>
internal sealed class ReminderRecordMap : ClassMap<ReminderRecord>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReminderRecordMap"/> class.
    /// </summary>
    public ReminderRecordMap()
    {
        this.AutoMap(CultureInfo.InvariantCulture);
        this.Map(record => record.FirstSeenUtc).TypeConverterOption.Format("O");
        this.Map(record => record.LastSeenUtc).TypeConverterOption.Format("O");
        this.Map(record => record.LastSyncedUtc).TypeConverterOption.Format("O");
    }
}
