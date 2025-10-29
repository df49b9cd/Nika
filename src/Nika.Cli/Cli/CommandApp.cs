using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;

namespace Nika.Cli;

internal static class CommandApp
{
    private const uint DefaultPrefetch = 10;
    private const uint DefaultLockTimeoutSeconds = 15;

    public static Parser Build(string version, CancellationToken cancellationToken)
    {
        var sourceOption = new Option<string?>("--source", "Location of the migrations (driver://url)")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        sourceOption.AddAlias("-source");

        var pathOption = new Option<string?>("--path", "Shorthand for --source=file://path")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        pathOption.AddAlias("-path");

        var databaseOption = new Option<string?>("--database", "Database connection (driver://url)")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        databaseOption.AddAlias("-database");

        var prefetchOption = new Option<uint>("--prefetch", () => DefaultPrefetch, "Number of migrations to prefetch")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        prefetchOption.AddAlias("-prefetch");

        var lockTimeoutOption = new Option<uint>("--lock-timeout", () => DefaultLockTimeoutSeconds, "Seconds to wait for database lock")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        lockTimeoutOption.AddAlias("-lock-timeout");

        var verboseOption = new Option<bool>("--verbose", "Enable verbose output");
        verboseOption.AddAlias("-verbose");

        var root = new RootCommand("Nika database migration CLI compatible with golang-migrate")
        {
            TreatUnmatchedTokensAsErrors = true,
        };

        root.AddGlobalOption(sourceOption);
        root.AddGlobalOption(pathOption);
        root.AddGlobalOption(databaseOption);
        root.AddGlobalOption(prefetchOption);
        root.AddGlobalOption(lockTimeoutOption);
        var versionOption = new Option<bool>("--version", "Print CLI version and exit");
        versionOption.AddAlias("-version");

        root.AddGlobalOption(verboseOption);
        root.AddGlobalOption(versionOption);

        var optionsReader = new GlobalOptionsReader(
            sourceOption,
            pathOption,
            databaseOption,
            prefetchOption,
            lockTimeoutOption,
            verboseOption);

        RegisterCommands(root, optionsReader);

        var builder = new CommandLineBuilder(root)
            .UseHelp()
            .UseParseDirective()
            .UseParseErrorReporting()
            .UseSuggestDirective()
            .RegisterWithDotnetSuggest()
            .UseTypoCorrections()
            .UseExceptionHandler()
            .UseEnvironmentVariableDirective()
            .AddMiddleware(async (context, next) =>
            {
                context.BindingContext.AddService(typeof(CancellationToken), _ => cancellationToken);

                if (context.ParseResult.GetValueForOption(versionOption))
                {
                    context.Console.Out.Write(version + Environment.NewLine);
                    context.ExitCode = 0;
                    return;
                }

                await next(context);
            });

        return builder.Build();
    }

    private static void RegisterCommands(RootCommand root, GlobalOptionsReader optionsReader)
    {
        var upCommand = CreateUpCommand(optionsReader);
        var downCommand = CreateDownCommand(optionsReader);
        var dropCommand = CreateDropCommand(optionsReader);
        var gotoCommand = CreateGotoCommand(optionsReader);
        var forceCommand = CreateForceCommand(optionsReader);
        var versionCommand = CreateVersionCommand(optionsReader);
        var createCommand = CreateCreateCommand(optionsReader);
        var stepsCommand = CreateStepsCommand(optionsReader);

        root.Add(upCommand);
        root.Add(downCommand);
        root.Add(dropCommand);
        root.Add(gotoCommand);
        root.Add(forceCommand);
        root.Add(versionCommand);
        root.Add(createCommand);
        root.Add(stepsCommand);
    }

    private static Command CreateUpCommand(GlobalOptionsReader optionsReader)
    {
        var stepsArgument = new Argument<int?>("n", () => null, "Apply only the next N up migrations")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        var command = new Command("up", "Apply all or N up migrations")
        {
            stepsArgument,
        };

        command.SetHandler(async context =>
        {
            var options = optionsReader.Read(context.ParseResult);
            var steps = context.ParseResult.GetValueForArgument(stepsArgument);
            await MigrationCommandHandlers.HandleUpAsync(options, steps, context);
        });

        return command;
    }

    private static Command CreateDownCommand(GlobalOptionsReader optionsReader)
    {
        var stepsArgument = new Argument<int?>("n", () => null, "Apply N down migrations")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        var allOption = new Option<bool>("--all", "Apply all down migrations");
        allOption.AddAlias("-all");

        var command = new Command("down", "Apply all or N down migrations")
        {
            stepsArgument,
        };
        command.AddOption(allOption);

        command.SetHandler(async context =>
        {
            var options = optionsReader.Read(context.ParseResult);
            var steps = context.ParseResult.GetValueForArgument(stepsArgument);
            var applyAll = context.ParseResult.GetValueForOption(allOption);
            await MigrationCommandHandlers.HandleDownAsync(options, steps, applyAll, context);
        });

        return command;
    }

    private static Command CreateDropCommand(GlobalOptionsReader optionsReader)
    {
        var forceOption = new Option<bool>("--force", "Drop schema without confirmation prompt");
        forceOption.AddAlias("-f");

        var command = new Command("drop", "Drop everything inside database");
        command.AddOption(forceOption);

        command.SetHandler(async context =>
        {
            var options = optionsReader.Read(context.ParseResult);
            var force = context.ParseResult.GetValueForOption(forceOption);
            await MigrationCommandHandlers.HandleDropAsync(options, force, context);
        });

        return command;
    }

    private static Command CreateGotoCommand(GlobalOptionsReader optionsReader)
    {
        var versionArgument = new Argument<int>("v", "Migrate to version V");

        var command = new Command("goto", "Migrate to version V")
        {
            versionArgument,
        };

        command.SetHandler(async context =>
        {
            var options = optionsReader.Read(context.ParseResult);
            var targetVersion = context.ParseResult.GetValueForArgument(versionArgument);
            await MigrationCommandHandlers.HandleGotoAsync(options, targetVersion, context);
        });

        return command;
    }

    private static Command CreateForceCommand(GlobalOptionsReader optionsReader)
    {
        var versionArgument = new Argument<int>("v", "Set version V but do not run migrations");

        var command = new Command("force", "Set the migration version without running migrations")
        {
            versionArgument,
        };

        command.SetHandler(async context =>
        {
            var options = optionsReader.Read(context.ParseResult);
            var targetVersion = context.ParseResult.GetValueForArgument(versionArgument);
            await MigrationCommandHandlers.HandleForceAsync(options, targetVersion, context);
        });

        return command;
    }

    private static Command CreateVersionCommand(GlobalOptionsReader optionsReader)
    {
        var command = new Command("version", "Print current migration version");

        command.SetHandler(async context =>
        {
            var options = optionsReader.Read(context.ParseResult);
            await MigrationCommandHandlers.HandleVersionAsync(options, context);
        });

        return command;
    }

    private static Command CreateStepsCommand(GlobalOptionsReader optionsReader)
    {
        var stepsArgument = new Argument<int>("n", "Move forward/backward by N steps");

        var command = new Command("steps", "Move forward/backward by N steps")
        {
            stepsArgument,
        };

        command.SetHandler(async context =>
        {
            var options = optionsReader.Read(context.ParseResult);
            var steps = context.ParseResult.GetValueForArgument(stepsArgument);
            await MigrationCommandHandlers.HandleStepsAsync(options, steps, context);
        });

        return command;
    }

    private static Command CreateCreateCommand(GlobalOptionsReader optionsReader)
    {
        var nameArgument = new Argument<string>("name", "Title of the new migration");

        var extensionOption = new Option<string>("--ext", "File extension for migration files")
        {
            Arity = ArgumentArity.ExactlyOne,
            IsRequired = true,
        };
        extensionOption.AddAlias("-ext");

        var directoryOption = new Option<string>("--dir", () => ".", "Target directory for the new migration files");
        directoryOption.AddAlias("-dir");

        var sequentialOption = new Option<bool>("--seq", "Generate sequential migrations instead of timestamped");
        sequentialOption.AddAlias("-seq");

        var digitsOption = new Option<int>("--digits", () => 6, "Number of digits to use for sequential migrations");
        digitsOption.AddAlias("-digits");

        var formatOption = new Option<string>("--format", () => MigrationFileScaffolder.DefaultTimestampFormat, "Go-style time format for timestamped migrations");
        formatOption.AddAlias("-format");

        var timezoneOption = new Option<string>("--tz", () => "UTC", "Time zone used for timestamped migrations");
        timezoneOption.AddAlias("-tz");

        var command = new Command("create", "Create a set of timestamped up/down migrations")
        {
            nameArgument,
        };

        command.AddOption(extensionOption);
        command.AddOption(directoryOption);
        command.AddOption(sequentialOption);
        command.AddOption(digitsOption);
        command.AddOption(formatOption);
        command.AddOption(timezoneOption);

        command.SetHandler(async context =>
        {
            var options = optionsReader.Read(context.ParseResult);
            var name = context.ParseResult.GetValueForArgument(nameArgument);
            var extension = context.ParseResult.GetValueForOption(extensionOption) ?? string.Empty;
            var directory = context.ParseResult.GetValueForOption(directoryOption) ?? ".";
            var sequential = context.ParseResult.GetValueForOption(sequentialOption);
            var digits = context.ParseResult.GetValueForOption(digitsOption);
            var format = context.ParseResult.GetValueForOption(formatOption) ?? MigrationFileScaffolder.DefaultTimestampFormat;
            var timezone = context.ParseResult.GetValueForOption(timezoneOption) ?? "UTC";

            await MigrationCommandHandlers.HandleCreateAsync(
                name,
                extension,
                directory,
                sequential,
                digits,
                format,
                timezone,
                context);
        });

        return command;
    }
}
