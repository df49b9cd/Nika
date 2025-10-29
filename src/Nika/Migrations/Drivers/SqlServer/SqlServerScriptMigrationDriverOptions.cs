using System;
using Microsoft.Data.SqlClient;

namespace Nika.Migrations.Drivers.SqlServer;

public sealed class SqlServerScriptMigrationDriverOptions
{
    private TimeSpan _commandTimeout = TimeSpan.FromSeconds(30);

    public SqlServerScriptMigrationDriverOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        ConnectionString = connectionString;

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource))
            {
                throw new ArgumentException("Connection string must specify a data source.", nameof(connectionString));
            }
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid SQL Server connection string.", nameof(connectionString), ex);
        }
    }

    public string ConnectionString { get; }

    public string? MigrationsSchema { get; init; }

    public string? MigrationsTable { get; init; }

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

    public bool UseTransactions { get; init; } = true;
}
