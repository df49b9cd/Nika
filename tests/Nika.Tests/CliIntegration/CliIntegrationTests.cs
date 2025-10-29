using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using Nika.Cli;
using Nika.Migrations;
using Nika.Migrations.Drivers.Postgres;
using Nika.Migrations.Drivers.SqlServer;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Nika.Tests.CliIntegration;

public sealed class CliIntegrationTests(PostgresContainerFixture postgres, SqlServerContainerFixture sqlServer)
    : IClassFixture<PostgresContainerFixture>, IClassFixture<SqlServerContainerFixture>
{
    [Fact]
    public async Task Postgres_UpDownVersion()
    {
        //EnsureSupported();

        var token = TestContext.Current.CancellationToken;
        var parser = CommandApp.Build("test", token);

        var (directory, sourceUri) = CopyMigrations();
        try
        {
            var console = new TestConsole();
            await parser.InvokeAsync([
                "up",
                "--source", sourceUri,
                "--database", postgres.ConnectionString
            ], console);

            console = new TestConsole();
            await parser.InvokeAsync([
                "version",
                "--source", sourceUri,
                "--database", postgres.ConnectionString
            ], console);

            Assert.Contains("(clean)", console.Out.ToString() ?? string.Empty);

            console = new TestConsole();
            await parser.InvokeAsync([
                "down",
                "1",
                "--source", sourceUri,
                "--database", postgres.ConnectionString
            ], console);

            var state = await postgres.GetStateAsync();
            Assert.Equal(2, state.Version);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [Fact]
    public async Task SqlServer_DropForce()
    {
        //EnsureSupported();

        var token = TestContext.Current.CancellationToken;
        var parser = CommandApp.Build("test", token);

        var (directory, sourceUri) = CopyMigrations();
        try
        {
            await parser.InvokeAsync([
                "up",
                "--source", sourceUri,
                "--database", sqlServer.ConnectionString
            ], new TestConsole());

            await parser.InvokeAsync([
                "drop",
                "--force",
                "--source", sourceUri,
                "--database", sqlServer.ConnectionString
            ], new TestConsole());

            var state = await sqlServer.GetStateAsync();
            Assert.Null(state.Version);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static void EnsureSupported()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Testcontainers-based CLI tests require Linux environment.");
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
        {
            Assert.Skip("Skipping container-backed CLI integration tests outside CI runner.");
        }
    }

    private static (string Directory, string SourceUri) CopyMigrations()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var examples = Path.Combine(sourceRoot, "examples", "migrations");
        var target = Path.Combine(Path.GetTempPath(), "nika-cli-it", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(examples))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)!));
        }

        var uri = new Uri(Path.EndsInDirectorySeparator(target) ? target : target + Path.DirectorySeparatorChar).AbsoluteUri;
        return (target, uri);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
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

    public async Task<MigrationVersionState> GetStateAsync()
    {
        await using var driver = new PostgresScriptMigrationDriver(
            new PostgresScriptMigrationDriverOptions(ConnectionString));
        return await driver.GetVersionStateAsync(default);
    }
}

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("YourStrong!Passw0rd")
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

    public async Task<MigrationVersionState> GetStateAsync()
    {
        await using var driver = new SqlServerScriptMigrationDriver(
            new SqlServerScriptMigrationDriverOptions(ConnectionString));
        return await driver.GetVersionStateAsync(default);
    }
}
