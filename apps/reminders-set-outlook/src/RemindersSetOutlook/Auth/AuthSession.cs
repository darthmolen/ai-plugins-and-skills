// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemindersSetOutlook.Configuration;

namespace RemindersSetOutlook.Auth;

/// <summary>
/// Owns the signed-in user's credential: a public client using delegated <c>Tasks.ReadWrite</c>,
/// with its tokens in the OS-protected MSAL cache and the (non-secret) account record in the state directory.
/// The first run prompts; later runs refresh silently until the refresh token lapses.
/// </summary>
internal sealed partial class AuthSession
{
    private const string TokenCacheName = "reminders-set-outlook";

    private readonly AuthOptions options;
    private readonly AppPaths paths;
    private readonly ILogger<AuthSession> logger;
    private TokenCredential? credential;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthSession"/> class.
    /// </summary>
    /// <param name="options">The auth options.</param>
    /// <param name="paths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public AuthSession(IOptions<AuthOptions> options, AppPaths paths, ILogger<AuthSession> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
        this.paths = paths;
        this.logger = logger;
    }

    /// <summary>
    /// Gets the delegated Microsoft Graph scopes the application asks for.
    /// </summary>
    public static string[] Scopes { get; } = { "https://graph.microsoft.com/Tasks.ReadWrite" };

    /// <summary>
    /// Returns the credential, prompting for sign-in first when there is no saved account and prompting is allowed.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The credential.</returns>
    /// <exception cref="InvalidOperationException">Not signed in and <see cref="AuthOptions.AllowInteractive"/> is off.</exception>
    public async Task<TokenCredential> GetCredentialAsync(CancellationToken cancellationToken)
    {
        if (this.credential is not null)
        {
            return this.credential;
        }

        var record = await this.LoadRecordAsync(cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            if (!this.options.AllowInteractive)
            {
                throw new InvalidOperationException(
                    "Not signed in, and Auth:AllowInteractive is false. Run 'reminders-set-outlook login' once.");
            }

            await this.LoginAsync(cancellationToken).ConfigureAwait(false);
            return this.credential!;
        }

        this.credential = this.Create(record);
        return this.credential;
    }

    /// <summary>
    /// Signs in interactively, whatever is cached, and saves the account record.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The signed-in account.</returns>
    public async Task<AuthenticationRecord> LoginAsync(CancellationToken cancellationToken)
    {
        var fresh = this.Create(record: null, interactive: true);
        var context = new TokenRequestContext(Scopes);
        this.LogSigningIn(this.options.Mode);
        var record = fresh switch
        {
            InteractiveBrowserCredential browser => await browser.AuthenticateAsync(context, cancellationToken).ConfigureAwait(false),
            DeviceCodeCredential deviceCode => await deviceCode.AuthenticateAsync(context, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported credential {fresh.GetType().Name}."),
        };

        Directory.CreateDirectory(this.paths.StateDirectory);
        var stream = File.Create(this.paths.AuthRecordFile);
        await using (stream.ConfigureAwait(false))
        {
            await record.SerializeAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        this.credential = fresh;
        this.LogSignedIn(record.Username, record.TenantId);
        return record;
    }

    /// <summary>
    /// Forgets the saved account so the next run signs in again.
    /// </summary>
    /// <returns><c>true</c> when an account record was removed.</returns>
    public bool Logout()
    {
        this.credential = null;
        if (!File.Exists(this.paths.AuthRecordFile))
        {
            return false;
        }

        File.Delete(this.paths.AuthRecordFile);
        return true;
    }

    /// <summary>
    /// Reads the saved account record.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record, or <c>null</c> when nobody has signed in.</returns>
    public async Task<AuthenticationRecord?> LoadRecordAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(this.paths.AuthRecordFile))
        {
            return null;
        }

        var stream = File.OpenRead(this.paths.AuthRecordFile);
        await using (stream.ConfigureAwait(false))
        {
            return await AuthenticationRecord.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    private TokenCredential Create(AuthenticationRecord? record, bool interactive = false)
    {
        if (string.IsNullOrWhiteSpace(this.options.ClientId))
        {
            throw new InvalidOperationException(
                "Auth:ClientId is not set. Register an application first; see docs/authentication.md.");
        }

        var cache = new TokenCachePersistenceOptions
        {
            Name = TokenCacheName,
            UnsafeAllowUnencryptedStorage = this.options.AllowUnencryptedTokenCache,
        };

        // When prompting is off, a lapsed refresh token surfaces as AuthenticationRequiredException instead of a prompt.
        var disableAutomatic = !interactive && !this.options.AllowInteractive;

        if (this.options.Mode == AuthMode.DeviceCode)
        {
            return new DeviceCodeCredential(new DeviceCodeCredentialOptions
            {
                ClientId = this.options.ClientId,
                TenantId = this.options.TenantId,
                TokenCachePersistenceOptions = cache,
                AuthenticationRecord = record,
                DisableAutomaticAuthentication = disableAutomatic,
                DeviceCodeCallback = (info, _) =>
                {
                    Console.WriteLine(info.Message);
                    return Task.CompletedTask;
                },
            });
        }

        return new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            ClientId = this.options.ClientId,
            TenantId = this.options.TenantId,
            RedirectUri = new Uri("http://localhost"),
            TokenCachePersistenceOptions = cache,
            AuthenticationRecord = record,
            DisableAutomaticAuthentication = disableAutomatic,
            LoginHint = record?.Username,
        });
    }

    [LoggerMessage(LogLevel.Information, "Signing in to Microsoft 365 using {Mode}")]
    private partial void LogSigningIn(AuthMode mode);

    [LoggerMessage(LogLevel.Information, "Signed in as {Username} (tenant {TenantId})")]
    private partial void LogSignedIn(string username, string tenantId);
}
