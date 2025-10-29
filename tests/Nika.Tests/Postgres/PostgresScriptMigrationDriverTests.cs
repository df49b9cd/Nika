using System;
using System.Threading;
using System.Threading.Tasks;
using Nika.Migrations;
using Nika.Migrations.Drivers.Postgres;
using Nika.Migrations.Sources;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Nika.Tests;

[CollectionDefinition("postgres-container")]
public sealed class PostgresContainerCollection : ICollectionFixture<PostgresContainerFixture>
{
}

[Collection("postgres-container")]
public sealed class PostgresScriptMigrationDriverTests(PostgresContainerFixture fixture)
{
    private readonly PostgresContainerFixture _fixture = fixture;
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExecutesScriptsAgainstPostgresContainer()
    {
        await _fixture.ResetSchemaAsync(TestToken);

        var options = new PostgresScriptMigrationDriverOptions(_fixture.ConnectionString)
        {
            CommandTimeout = TimeSpan.FromSeconds(30),
            SearchPath = "public",
        };

        await using var driver = new PostgresScriptMigrationDriver(options);

        var projectDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var repositoryRoot = Path.GetFullPath(Path.Combine(projectDirectory, "..", ".."));
        var migrationsDirectory = Path.Combine(repositoryRoot, "examples", "migrations");

        var source = new FileSystemMigrationSource(migrationsDirectory);
        var runner = MigrationEngine.New(source, driver);

        await runner.StepsAsync(3, TestToken);

        await AssertTableExistsAsync(_fixture.ConnectionString, expectedExists: true, TestToken);
        await AssertColumnExistsAsync(_fixture.ConnectionString, "users", "city", expectedExists: true, TestToken);
        await AssertIndexExistsAsync(_fixture.ConnectionString, "users_email_index", expectedExists: true, TestToken);

        await runner.DownAsync(3, TestToken);

        await AssertTableExistsAsync(_fixture.ConnectionString, expectedExists: false, TestToken);

        var finalState = await driver.GetVersionStateAsync(TestToken);
        Assert.Null(finalState.Version);
        Assert.False(finalState.IsDirty);
    }

    private static async Task AssertTableExistsAsync(string connectionString, bool expectedExists, CancellationToken cancellationToken)
    {
        const string sql = """
        SELECT COUNT(*)
        FROM information_schema.tables
        WHERE table_schema = 'public'
          AND table_name = 'users';
        """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

        Assert.Equal(expectedExists ? 1 : 0, count);
    }

    private static async Task AssertColumnExistsAsync(string connectionString, string table, string column, bool expectedExists, CancellationToken cancellationToken)
    {
        const string sql = """
        SELECT COUNT(*)
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = @table
          AND column_name = @column;
        """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);

        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        Assert.Equal(expectedExists ? 1 : 0, count);
    }

    private static async Task AssertIndexExistsAsync(string connectionString, string indexName, bool expectedExists, CancellationToken cancellationToken)
    {
        const string sql = """
        SELECT COUNT(*)
        FROM pg_indexes
        WHERE schemaname = 'public'
          AND indexname = @index;
        """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@index", indexName);

        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        Assert.Equal(expectedExists ? 1 : 0, count);
    }
}

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithDatabase("nika")
        .WithUsername("nika")
        .WithPassword("nika")
        .WithImage("postgres:16-alpine")
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

    public async Task ResetSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = "DROP TABLE IF EXISTS nika_driver_test;";
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
