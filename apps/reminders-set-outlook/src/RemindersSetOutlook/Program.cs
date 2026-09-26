// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RemindersSetOutlook.Auth;
using RemindersSetOutlook.Cli;
using RemindersSetOutlook.Configuration;
using RemindersSetOutlook.Scanning;
using RemindersSetOutlook.State;
using RemindersSetOutlook.Sync;
using RemindersSetOutlook.Todo;
using Serilog;
using Serilog.Events;

namespace RemindersSetOutlook;

/// <summary>
/// The application entry point.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Parses the command line, builds the host and runs the command.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        CommandLine command;
        try
        {
            command = CommandLine.Parse(args);
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            await Console.Error.WriteLineAsync(CommandLine.Usage).ConfigureAwait(false);
            return 2;
        }

        if (command.Verb == CommandVerb.Help)
        {
            Console.WriteLine(CommandLine.Usage);
            return 0;
        }

        using var host = BuildHost(command);
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var runner = host.Services.GetRequiredService<CommandRunner>();
        return await runner.RunAsync(command, cancellation.Token).ConfigureAwait(false);
    }

    private static IHost BuildHost(CommandLine command)
    {
        var configurationArgs = command.ConfigurationArgs.ToArray();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = configurationArgs,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Layering: shipped appsettings.json < per-user appsettings.json < environment < command line.
        builder.Configuration.AddJsonFile(AppPaths.UserConfigFile, optional: true, reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables("REMINDERS_OUTLOOK_");
        builder.Configuration.AddCommandLine(configurationArgs);

        builder.Services.Configure<ScanOptions>(builder.Configuration.GetSection(ScanOptions.SectionName));
        builder.Services.Configure<TodoOptions>(builder.Configuration.GetSection(TodoOptions.SectionName));
        builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
        builder.Services.Configure<StateOptions>(builder.Configuration.GetSection(StateOptions.SectionName));
        builder.Services.Configure<WatchOptions>(builder.Configuration.GetSection(WatchOptions.SectionName));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<AppPaths>();
        builder.Services.AddSingleton<AuthSession>();
        builder.Services.AddSingleton<ReminderScanner>();
        builder.Services.AddSingleton<ReminderStateStore>();
        builder.Services.AddSingleton<TaskDraftBuilder>();
        builder.Services.AddSingleton<SyncEngine>();
        builder.Services.AddSingleton<CommandRunner>();

        // The engine is a singleton that lives as long as "watch" does, so it keeps one client; the pooled
        // connection lifetime, not handler rotation, is what keeps DNS fresh.
        builder.Services.AddHttpClient<ITodoGateway, GraphTodoGateway>(client => client.BaseAddress = GraphTodoGateway.BaseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        builder.Services.AddSerilog((services, logging) =>
        {
            var paths = services.GetRequiredService<AppPaths>();
            logging
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
                .ReadFrom.Configuration(builder.Configuration)
                .WriteTo.Console(
                    outputTemplate: "{Timestamp:HH:mm:ss} {Level:u3} {Message:lj}{NewLine}{Exception}",
                    formatProvider: CultureInfo.InvariantCulture)
                .WriteTo.File(
                    Path.Combine(paths.LogDirectory, "reminders-set-outlook-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    formatProvider: CultureInfo.InvariantCulture);
        });

        return builder.Build();
    }
}
