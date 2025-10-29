using System.CommandLine.Invocation;
using System.Globalization;
using Nika.Migrations;

namespace Nika.Cli;

internal static class MigrationCommandHandlers
{
    public static async Task HandleUpAsync(GlobalOptions options, int? steps, InvocationContext context)
    {
        await ExecuteWithSessionAsync(options, context, async (session, token) =>
        {
            if (steps.HasValue)
            {
                if (steps.Value <= 0)
                {
                    throw new CliUsageException("Argument N must be a positive integer.");
                }

                await session.Runner.UpAsync(steps.Value, token).ConfigureAwait(false);
                WriteLine(context, $"Applied {steps.Value} migration(s).");
            }
            else
            {
                await session.Runner.UpAsync(token).ConfigureAwait(false);
                WriteLine(context, "Applied all pending migrations.");
            }

            await ReportStateAsync(session.Runner, context, token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public static async Task HandleDownAsync(GlobalOptions options, int? steps, bool all, InvocationContext context)
    {
        if (all && steps.HasValue)
        {
            WriteError(context, "Specify either --all or N, but not both.");
            context.ExitCode = 2;
            return;
        }

        await ExecuteWithSessionAsync(options, context, async (session, token) =>
        {
            if (all)
            {
                if (!await ConfirmAsync("Are you sure you want to apply all down migrations? [y/N] ", context, token).ConfigureAwait(false))
                {
                    throw new CliUsageException("Not applying all down migrations.");
                }

                await session.Runner.DownAsync(int.MaxValue, token).ConfigureAwait(false);
                WriteLine(context, "Applied all down migrations.");
            }
            else if (steps.HasValue)
            {
                if (steps.Value <= 0)
                {
                    throw new CliUsageException("Argument N must be a positive integer.");
                }

                await session.Runner.DownAsync(steps.Value, token).ConfigureAwait(false);
                WriteLine(context, $"Reverted {steps.Value} migration(s).");
            }
            else
            {
                await session.Runner.DownAsync(token).ConfigureAwait(false);
                WriteLine(context, "Reverted 1 migration.");
            }

            await ReportStateAsync(session.Runner, context, token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public static async Task HandleDropAsync(GlobalOptions options, bool force, InvocationContext context)
    {
        await ExecuteWithSessionAsync(options, context, async (session, token) =>
        {
            if (!force)
            {
                if (!await ConfirmAsync("Are you sure you want to drop the entire database schema? [y/N] ", context, token).ConfigureAwait(false))
                {
                    throw new CliUsageException("Aborted dropping the entire database schema.");
                }
            }

            await session.Runner.DropAsync(force, token).ConfigureAwait(false);
            WriteLine(context, "Dropped database schema.");
        }).ConfigureAwait(false);
    }

    public static async Task HandleGotoAsync(GlobalOptions options, int version, InvocationContext context)
    {
        if (version < 0)
        {
            WriteError(context, "Version must be a non-negative integer.");
            context.ExitCode = 2;
            return;
        }

        await ExecuteWithSessionAsync(options, context, async (session, token) =>
        {
            await session.Runner.GotoAsync(version, token).ConfigureAwait(false);
            WriteLine(context, $"Moved to version {version}.");
            await ReportStateAsync(session.Runner, context, token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public static async Task HandleForceAsync(GlobalOptions options, int version, InvocationContext context)
    {
        if (version < -1)
        {
            WriteError(context, "Version must be >= -1.");
            context.ExitCode = 2;
            return;
        }

        await ExecuteWithSessionAsync(options, context, async (session, token) =>
        {
            await session.Runner.ForceAsync(version, token).ConfigureAwait(false);
            WriteLine(context, $"Forced migration version to {(version == -1 ? "baseline" : version.ToString(CultureInfo.InvariantCulture))}.");
            await ReportStateAsync(session.Runner, context, token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public static async Task HandleVersionAsync(GlobalOptions options, InvocationContext context)
    {
        await ExecuteWithSessionAsync(options, context, async (session, token) =>
        {
            await ReportStateAsync(session.Runner, context, token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public static async Task HandleStepsAsync(GlobalOptions options, int steps, InvocationContext context)
    {
        if (steps == 0)
        {
            WriteError(context, "Argument N must not be zero.");
            context.ExitCode = 2;
            return;
        }

        await ExecuteWithSessionAsync(options, context, async (session, token) =>
        {
            await session.Runner.StepsAsync(steps, token).ConfigureAwait(false);
            WriteLine(context, $"Executed migration steps: {steps}.");
            await ReportStateAsync(session.Runner, context, token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public static async Task HandleCreateAsync(
        string name,
        string extension,
        string directory,
        bool sequential,
        int digits,
        string format,
        string timezone,
        InvocationContext context)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new CliUsageException("Migration name must be provided.");
            }

            if (string.IsNullOrWhiteSpace(extension))
            {
                throw new CliUsageException("--ext flag must be specified.");
            }

            var timeZone = ResolveTimeZone(timezone);
            var scaffolder = new MigrationFileScaffolder(directory);
            var cancellationToken = GetCancellationToken(context);

            if (sequential && !string.Equals(format, MigrationFileScaffolder.DefaultTimestampFormat, StringComparison.Ordinal))
            {
                throw new CliUsageException("The --seq and --format options are mutually exclusive.");
            }

            var result = await scaffolder.ScaffoldAsync(
                name,
                extension,
                sequential,
                digits,
                format,
                timeZone,
                cancellationToken).ConfigureAwait(false);

            foreach (var path in result.CreatedFiles)
            {
                WriteLine(context, path);
            }
        }
        catch (CliUsageException ex)
        {
            WriteError(context, ex.Message);
            context.ExitCode = 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WriteError(context, ex.Message);
            context.ExitCode = 1;
        }
    }

    private static async Task ExecuteWithSessionAsync(
        GlobalOptions options,
        InvocationContext context,
        Func<MigrationSession, CancellationToken, Task> action)
    {
        var cancellationToken = GetCancellationToken(context);

        try
        {
            await using var session = await MigrationSessionFactory.CreateAsync(options).ConfigureAwait(false);
            session.Runner.PrefetchCount = options.Prefetch > 0 ? (int)options.Prefetch : 0;
            session.Runner.Logger = options.Verbose
                ? message => WriteLine(context, message)
                : null;
            await action(session, cancellationToken).ConfigureAwait(false);
            session.Runner.Logger = null;
        }
        catch (CliUsageException ex)
        {
            WriteError(context, ex.Message);
            context.ExitCode = 2;
        }
        catch (DirtyMigrationStateException ex)
        {
            WriteError(context, $"Database is dirty: {ex.Message}");
            context.ExitCode = 2;
        }
        catch (MigrationException ex)
        {
            WriteError(context, $"Migration failed: {ex.Message}");
            context.ExitCode = 1;
        }
        catch (OperationCanceledException)
        {
            WriteError(context, "Operation cancelled.");
            context.ExitCode = 1;
        }
    }

    private static async Task ReportStateAsync(MigrationRunner runner, InvocationContext context, CancellationToken cancellationToken)
    {
        var state = await runner.GetVersionStateAsync(cancellationToken).ConfigureAwait(false);
        var versionText = state.Version?.ToString(CultureInfo.InvariantCulture) ?? "Nil";
        var dirtyText = state.IsDirty ? "dirty" : "clean";
        WriteLine(context, $"Current version: {versionText} ({dirtyText}).");
    }

    private static TimeZoneInfo ResolveTimeZone(string timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return TimeZoneInfo.Utc;
        }

        if (string.Equals(timezone, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new CliUsageException($"Unknown timezone '{timezone}'.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new CliUsageException($"Invalid timezone '{timezone}'.");
        }
    }

    private static async Task<bool> ConfirmAsync(string prompt, InvocationContext context, CancellationToken cancellationToken)
    {
        Write(context, prompt);
        string? response = Console.IsInputRedirected
            ? await Task.Run(() => Console.ReadLine(), cancellationToken).ConfigureAwait(false)
            : await Task.Run(() => Console.ReadLine(), cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return false;
        }

        response = response.Trim();
        return string.Equals(response, "y", StringComparison.OrdinalIgnoreCase);
    }

    private static CancellationToken GetCancellationToken(InvocationContext context)
    {
        var service = context.BindingContext.GetService(typeof(CancellationToken));
        return service is CancellationToken token ? token : CancellationToken.None;
    }

    private static void WriteLine(InvocationContext context, string message)
        => Write(context, message + Environment.NewLine);

    private static void WriteError(InvocationContext context, string message)
        => context.Console.Error.Write(message + Environment.NewLine);

    private static void Write(InvocationContext context, string message)
        => context.Console.Out.Write(message);
}
