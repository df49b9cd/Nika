using System.Data;
using Microsoft.Data.SqlClient;
using Nika.Migrations;
using Nika.Migrations.Drivers.SqlServer;
using Nika.Migrations.Sources;
using Testcontainers.MsSql;
using Xunit;

namespace Nika.Tests;

[CollectionDefinition("sqlserver-container")]
public sealed class SqlServerContainerCollection : ICollectionFixture<SqlServerContainerFixture>
{
}

[Collection("sqlserver-container")]
public sealed class SqlServerScriptMigrationDriverTests(SqlServerContainerFixture fixture)
{
    private readonly SqlServerContainerFixture _fixture = fixture;
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExecutesScriptsAgainstSqlServerContainer()
    {
        await _fixture.ResetDatabaseAsync(TestToken);

        var options = new SqlServerScriptMigrationDriverOptions(_fixture.ConnectionString)
        {
            CommandTimeout = TimeSpan.FromSeconds(30),
            UseTransactions = true,
        };

        await using var driver = new SqlServerScriptMigrationDriver(options);

        var projectDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var migrationsDirectory = Path.Combine(projectDirectory, "SqlServer", "examples", "migrations");

        var source = new FileSystemMigrationSource(migrationsDirectory);
        var runner = MigrationEngine.New(source, driver);

        await runner.StepsAsync(3, TestToken);

        Assert.True(await TableExistsAsync(_fixture.ConnectionString, "dbo", "users", TestToken));
        Assert.True(await ColumnExistsAsync(_fixture.ConnectionString, "dbo", "users", "city", TestToken));
        Assert.True(await IndexExistsAsync(_fixture.ConnectionString, "users_email_index", TestToken));

        await runner.DownAsync(3, TestToken);

        Assert.False(await TableExistsAsync(_fixture.ConnectionString, "dbo", "users", TestToken));
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string schema, string table, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT COUNT(*)
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = @schema AND t.name = @table;
";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqlCommand(sql, connection)
        {
            CommandType = CommandType.Text,
        };

        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);

        var count = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
        return count > 0;
    }

    private static async Task<bool> ColumnExistsAsync(string connectionString, string schema, string table, string column, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT COUNT(*)
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table AND COLUMN_NAME = @column;
";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);

        var count = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
        return count > 0;
    }

    private static async Task<bool> IndexExistsAsync(string connectionString, string indexName, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT COUNT(*) FROM sys.indexes WHERE name = @name;";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@name", indexName);

        var count = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0);
        return count > 0;
    }
}

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private const string Password = "YourStrong!Passw0rd";
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithPassword(Password)
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = "IF OBJECT_ID('dbo.nika_driver_test', 'U') IS NOT NULL DROP TABLE dbo.nika_driver_test;";
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
