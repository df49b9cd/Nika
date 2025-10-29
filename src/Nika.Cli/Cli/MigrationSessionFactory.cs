using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Nika.Migrations;
using Nika.Migrations.Drivers.Postgres;
using Nika.Migrations.Drivers.SqlServer;
using Nika.Migrations.Sources;
using Npgsql;

namespace Nika.Cli;

internal sealed class MigrationSession(IMigrationSource source, IMigrationDriver driver, MigrationRunner runner) : IAsyncDisposable
{
    public IMigrationSource Source { get; } = source;

    public IMigrationDriver Driver { get; } = driver;

    public MigrationRunner Runner { get; } = runner;

    public ValueTask DisposeAsync()
        => Driver.DisposeAsync();
}

internal static class MigrationSessionFactory
{
    public static async Task<MigrationSession> CreateAsync(GlobalOptions options, CancellationToken cancellationToken)
    {
        var source = CreateSource(options);
        var driver = CreateDriver(options);
        var runner = MigrationEngine.New(source, driver);
        return await Task.FromResult(new MigrationSession(source, driver, runner)).ConfigureAwait(false);
    }

    private static IMigrationSource CreateSource(GlobalOptions options)
    {
        var sourceValue = options.Source;

        if (string.IsNullOrWhiteSpace(sourceValue) && !string.IsNullOrWhiteSpace(options.Path))
        {
            var fullPath = Path.GetFullPath(options.Path);
            sourceValue = new Uri(fullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? fullPath
                : fullPath + Path.DirectorySeparatorChar).ToString();
        }

        if (string.IsNullOrWhiteSpace(sourceValue))
        {
            throw new CliUsageException("A --source or --path option must be specified.");
        }

        if (!Uri.TryCreate(sourceValue, UriKind.Absolute, out var parsed))
        {
            throw new CliUsageException($"Invalid source URI '{sourceValue}'.");
        }

        if (!string.Equals(parsed.Scheme, "file", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException($"Unsupported source scheme '{parsed.Scheme}'. Only file:// is supported.");
        }

        var directory = parsed.LocalPath;
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new CliUsageException("Source directory must not be empty.");
        }

        directory = Path.GetFullPath(directory);
        return new FileSystemMigrationSource(directory);
    }

    private static IMigrationDriver CreateDriver(GlobalOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Database))
        {
            throw new CliUsageException("The --database option must be provided.");
        }

        if (!Uri.TryCreate(options.Database, UriKind.Absolute, out var parsed))
        {
            throw new CliUsageException($"Invalid database URI '{options.Database}'.");
        }

        return parsed.Scheme switch
        {
            "postgres" or "postgresql" => CreatePostgresDriver(parsed, options),
            "sqlserver" or "mssql" => CreateSqlServerDriver(parsed, options),
            _ => throw new CliUsageException($"Unsupported database scheme '{parsed.Scheme}'."),
        };
    }

    private static IMigrationDriver CreatePostgresDriver(Uri uri, GlobalOptions options)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = string.IsNullOrWhiteSpace(uri.Host) ? "localhost" : uri.Host,
        };

        if (!uri.IsDefaultPort && uri.Port > 0)
        {
            builder.Port = uri.Port;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1)
            {
                builder.Password = Uri.UnescapeDataString(parts[1]);
            }
        }

        var path = uri.AbsolutePath.Trim('/');
        if (!string.IsNullOrEmpty(path))
        {
            builder.Database = Uri.UnescapeDataString(path);
        }

        string? searchPath = null;

        foreach (var (key, value) in ParseQuery(uri.Query))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (string.Equals(key, "search_path", StringComparison.OrdinalIgnoreCase))
            {
                searchPath = value;
                continue;
            }

            builder[key] = value;
        }

        var commandTimeout = ToCommandTimeout(options.LockTimeout);

        var driverOptions = new PostgresScriptMigrationDriverOptions(builder.ConnectionString)
        {
            CommandTimeout = commandTimeout,
            SearchPath = searchPath,
            UseTransactions = false,
        };

        return new PostgresScriptMigrationDriver(driverOptions);
    }

    private static IMigrationDriver CreateSqlServerDriver(Uri uri, GlobalOptions options)
    {
        var builder = new SqlConnectionStringBuilder();

        if (!string.IsNullOrWhiteSpace(uri.Host))
        {
            var dataSource = uri.Host;
            if (!uri.IsDefaultPort && uri.Port > 0)
            {
                dataSource += "," + uri.Port.ToString(CultureInfo.InvariantCulture);
            }

            builder.DataSource = dataSource;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            builder.UserID = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1)
            {
                builder.Password = Uri.UnescapeDataString(parts[1]);
            }
        }

        var path = uri.AbsolutePath.Trim('/');
        if (!string.IsNullOrWhiteSpace(path))
        {
            builder.InitialCatalog = Uri.UnescapeDataString(path);
        }

        foreach (var (key, value) in ParseQuery(uri.Query))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            builder[key] = value;
        }

        var driverOptions = new SqlServerScriptMigrationDriverOptions(builder.ConnectionString)
        {
            CommandTimeout = ToCommandTimeout(options.LockTimeout),
        };

        return new SqlServerScriptMigrationDriver(driverOptions);
    }

    private static TimeSpan ToCommandTimeout(uint seconds)
    {
        if (seconds == 0)
        {
            seconds = 1;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return Array.Empty<KeyValuePair<string, string>>().ToDictionary(k => k.Key, v => v.Value);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;

            result[key] = value;
        }

        return result;
    }
}

internal sealed class CliUsageException(string message) : Exception(message)
{
}
