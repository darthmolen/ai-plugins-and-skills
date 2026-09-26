// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

namespace RemindersSetOutlook.Configuration;

/// <summary>
/// Microsoft Entra sign-in settings, bound from the <c>Auth</c> configuration section.
/// See <c>docs/authentication.md</c> for why this is a public client with delegated permissions.
/// </summary>
internal sealed class AuthOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// Gets or sets the application (client) id of your own app registration.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the authority tenant: a tenant id, <c>organizations</c>, <c>consumers</c> or <c>common</c>.
    /// </summary>
    public string TenantId { get; set; } = "common";

    /// <summary>
    /// Gets or sets the interactive sign-in experience.
    /// </summary>
    public AuthMode Mode { get; set; } = AuthMode.Browser;

    /// <summary>
    /// Gets or sets a value indicating whether a sync may prompt for sign-in when the cached token cannot be refreshed.
    /// Set it to <c>false</c> for unattended runs, which then fail and ask for <c>login</c> instead of waiting on a prompt.
    /// </summary>
    public bool AllowInteractive { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the token cache may fall back to an unencrypted file
    /// when no OS secret store (DPAPI, Keychain, libsecret) is available.
    /// </summary>
    public bool AllowUnencryptedTokenCache { get; set; }
}
