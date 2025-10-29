using System;
using Npgsql;

namespace Nika.Migrations.Drivers.Postgres;

public sealed class PostgresScriptMigrationDriverOptions
{
    private TimeSpan _commandTimeout = TimeSpan.FromSeconds(30);
    private const int DefaultMultiStatementMaxSize = 10 * 1 << 20; // 10 MB

    public PostgresScriptMigrationDriverOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        ConnectionString = connectionString;

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.Host))
            {
                throw new ArgumentException("Connection string must specify a host name or address.", nameof(connectionString));
            }

            var database = builder.Database;
            if (string.IsNullOrWhiteSpace(database))
            {
                throw new ArgumentException("Connection string must specify a database name.", nameof(connectionString));
            }

            DatabaseName = database;
            ConnectionSearchPath = builder.SearchPath;
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid PostgreSQL connection string.", nameof(connectionString), ex);
        }
    }

    public string ConnectionString { get; }

    public string DatabaseName { get; }

    internal string? ConnectionSearchPath { get; }

    public TimeSpan CommandTimeout
    {
        get => _commandTimeout;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(CommandTimeout), value, "Command timeout must be greater than zero.");
            }

            _commandTimeout = value;
        }
    }

    public string? SearchPath { get; init; }

    public bool UseTransactions { get; init; } = false;

    public bool MultiStatementEnabled { get; init; } = false;

    public int MultiStatementMaxSize { get; init; } = DefaultMultiStatementMaxSize;

    public string MigrationsTable { get; init; } = "schema_migrations";

    public string? MigrationsSchema { get; init; }

    public bool UseAdvisoryLocks { get; init; } = true;

    public long AdvisoryLockKey { get; init; } = 0;
}
