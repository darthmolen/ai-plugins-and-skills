// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemindersSetOutlook.Auth;
using RemindersSetOutlook.Configuration;
using RemindersSetOutlook.State;
using RemindersSetOutlook.Sync;

namespace RemindersSetOutlook.Cli;

/// <summary>
/// Carries out a parsed command.
/// </summary>
internal sealed partial class CommandRunner
{
    private readonly AuthSession auth;
    private readonly SyncEngine engine;
    private readonly ReminderStateStore store;
    private readonly AppPaths paths;
    private readonly WatchOptions watch;
    private readonly ILogger<CommandRunner> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandRunner"/> class.
    /// </summary>
    /// <param name="auth">The auth session.</param>
    /// <param name="engine">The sync engine.</param>
    /// <param name="store">The findings store.</param>
    /// <param name="paths">The application paths.</param>
    /// <param name="watch">The watch options.</param>
    /// <param name="logger">The logger.</param>
    public CommandRunner(
        AuthSession auth,
        SyncEngine engine,
        ReminderStateStore store,
        AppPaths paths,
        IOptions<WatchOptions> watch,
        ILogger<CommandRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(watch);
        this.auth = auth;
        this.engine = engine;
        this.store = store;
        this.paths = paths;
        this.watch = watch.Value;
        this.logger = logger;
    }

    /// <summary>
    /// Runs the command.
    /// </summary>
    /// <param name="command">The parsed command line.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(CommandLine command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            return command.Verb switch
            {
                CommandVerb.Login => await this.LoginAsync(cancellationToken).ConfigureAwait(false),
                CommandVerb.Logout => this.Logout(),
                CommandVerb.Status => this.Status(),
                CommandVerb.Watch => await this.WatchAsync(command, cancellationToken).ConfigureAwait(false),
                _ => await this.SyncOnceAsync(command, cancellationToken).ConfigureAwait(false),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 130;
        }
        catch (AuthenticationRequiredException)
        {
            this.LogSignInRequired();
            return 3;
        }
        catch (AuthenticationFailedException ex)
        {
            this.LogAuthFailed(ex);
            return 3;
        }
        catch (InvalidOperationException ex)
        {
            this.LogConfigurationProblem(ex.Message);
            return 2;
        }
    }

    private async Task<int> LoginAsync(CancellationToken cancellationToken)
    {
        var record = await this.auth.LoginAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Signed in as {record.Username}. Unattended runs will now refresh silently.");
        return 0;
    }

    private int Logout()
    {
        Console.WriteLine(this.auth.Logout()
            ? "Signed out. The next run will ask you to sign in."
            : "Nobody was signed in.");
        return 0;
    }

    private int Status()
    {
        var records = this.store.Load().Values.ToList();
        Console.WriteLine($"Findings: {this.paths.CsvFile}");
        Console.WriteLine($"{records.Count} reminder(s) known.");
        foreach (var group in records.GroupBy(record => record.State).OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {group.Key,-8} {group.Count()}");
        }

        foreach (var record in records.Where(record => record.State is RecordState.Error or RecordState.Invalid))
        {
            Console.WriteLine($"  ! {record.Path}: {record.Message}");
        }

        return 0;
    }

    private async Task<int> SyncOnceAsync(CommandLine command, CancellationToken cancellationToken)
    {
        using var held = SyncLock.TryAcquire(this.paths.LockFile);
        if (held is null)
        {
            this.LogAlreadyRunning();
            return 4;
        }

        if (!command.DryRun)
        {
            // Sign in up front, so a needed prompt appears at startup rather than halfway through a scan.
            await this.auth.GetCredentialAsync(cancellationToken).ConfigureAwait(false);
        }

        var report = await this.engine.RunAsync(command.DryRun, command.Force, cancellationToken).ConfigureAwait(false);
        this.LogReport(report.ToString());
        return report.Failed > 0 ? 1 : 0;
    }

    private async Task<int> WatchAsync(CommandLine command, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, this.watch.IntervalMinutes));
        this.LogWatching(interval.TotalMinutes);
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await this.SyncOnceAsync(command, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException
                or (AuthenticationFailedException and not AuthenticationRequiredException))
            {
                // One bad pass (offline, a locked file, a dismissed sign-in) must not end the watch.
                // Only "sign-in required with prompting off" does, since no later pass can fix it.
                this.LogPassFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));

        return 0;
    }

    [LoggerMessage(LogLevel.Information, "Sync complete: {Summary}")]
    private partial void LogReport(string summary);

    [LoggerMessage(LogLevel.Warning, "Another sync is already running; skipping this one")]
    private partial void LogAlreadyRunning();

    [LoggerMessage(LogLevel.Information, "Watching; syncing every {Minutes} minute(s). Press Ctrl+C to stop")]
    private partial void LogWatching(double minutes);

    [LoggerMessage(LogLevel.Error, "This sync pass failed; the next one will retry")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "Sign-in is required and prompting is off. Run 'reminders-set-outlook login'")]
    private partial void LogSignInRequired();

    [LoggerMessage(LogLevel.Error, "Sign-in failed")]
    private partial void LogAuthFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "{Message}")]
    private partial void LogConfigurationProblem(string message);
}
