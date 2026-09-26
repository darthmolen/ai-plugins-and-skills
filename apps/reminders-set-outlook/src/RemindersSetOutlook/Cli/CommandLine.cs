// Copyright (c) darthmolen. All rights reserved.
// Licensed under the BSD-2-Clause license.

using System;
using System.Collections.Generic;

namespace RemindersSetOutlook.Cli;

/// <summary>
/// The parsed command line: a verb, its flags, and any <c>--Section:Key=value</c> configuration overrides.
/// </summary>
internal sealed class CommandLine
{
    /// <summary>
    /// The usage text.
    /// </summary>
    public const string Usage = """
        reminders-set-outlook - mirror reminders-set files into Microsoft To Do tasks

        Usage:
          reminders-set-outlook [command] [options] [--Section:Key=value ...]

        Commands:
          sync      Scan once, create or update tasks, then exit (default)
          watch     Sync now and then every Watch:IntervalMinutes until stopped
          login     Sign in interactively and remember the account
          logout    Forget the remembered account
          status    Summarise the findings CSV
          help      Show this text

        Options:
          --dry-run   Report what would change; write no tasks and no CSV
          --force     Rewrite every task, even when its file's hash is unchanged

        Any configuration value can be overridden, for example:
          reminders-set-outlook sync --Scan:Roots:0=C:\src --Todo:ListName=Work
        """;

    private CommandLine(CommandVerb verb, bool dryRun, bool force, string[] configurationArgs)
    {
        this.Verb = verb;
        this.DryRun = dryRun;
        this.Force = force;
        this.ConfigurationArgs = configurationArgs;
    }

    /// <summary>
    /// Gets the command.
    /// </summary>
    public CommandVerb Verb { get; }

    /// <summary>
    /// Gets a value indicating whether <c>--dry-run</c> was given.
    /// </summary>
    public bool DryRun { get; }

    /// <summary>
    /// Gets a value indicating whether <c>--force</c> was given.
    /// </summary>
    public bool Force { get; }

    /// <summary>
    /// Gets the arguments left for the configuration system.
    /// </summary>
    public IReadOnlyList<string> ConfigurationArgs { get; }

    /// <summary>
    /// Parses the command line.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The parsed command line.</returns>
    /// <exception cref="ArgumentException">An argument is not understood.</exception>
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var verb = CommandVerb.Sync;
        var dryRun = false;
        var force = false;
        var configuration = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg.ToUpperInvariant())
            {
                case "--DRY-RUN":
                    dryRun = true;
                    break;
                case "--FORCE":
                    force = true;
                    break;
                case "-H" or "--HELP" or "/?":
                    verb = CommandVerb.Help;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        if (!arg.Contains(':', StringComparison.Ordinal))
                        {
                            throw new ArgumentException($"Unknown option '{arg}'.");
                        }

                        configuration.Add(arg);
                        if (!arg.Contains('=', StringComparison.Ordinal) && index + 1 < args.Count)
                        {
                            configuration.Add(args[++index]);
                        }
                    }
                    else if (index == 0 && Enum.TryParse<CommandVerb>(arg, ignoreCase: true, out var parsed))
                    {
                        verb = parsed;
                    }
                    else
                    {
                        throw new ArgumentException($"Unknown command '{arg}'.");
                    }

                    break;
            }
        }

        return new CommandLine(verb, dryRun, force, configuration.ToArray());
    }
}
