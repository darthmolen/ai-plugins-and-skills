// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

namespace RemindersSetOutlook.Configuration;

/// <summary>
/// The interactive sign-in experience used when no cached token can be refreshed silently.
/// </summary>
internal enum AuthMode
{
    /// <summary>
    /// Opens the system browser and listens on a localhost redirect.
    /// </summary>
    Browser,

    /// <summary>
    /// Prints a code to enter at https://microsoft.com/devicelogin, for machines without a browser.
    /// </summary>
    DeviceCode,
}
