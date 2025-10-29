using System;
using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading.Tasks;
using Nika.Cli;
using Nika.Migrations;
using Nika.Migrations.Drivers.Postgres;
using Nika.Migrations.Drivers.SqlServer;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Nika.Tests.CliIntegration;

public sealed class CliIntegrationTests : IClassFixture<PostgresContainerFixture>, IClassFixture<SqlServerContainerFixture>
{
    private readonly PostgresContainerFixture _postgres;
    private readonly SqlServerContainerFixture _sqlServer;

    public CliIntegrationTests(PostgresContainerFixture postgres, SqlServerContainerFixture sqlServer)
    {
        _postgres = postgres;
        _sqlServer = sqlServer;
    }

    [Fact]
    public async Task Postgres_UpDownVersion()
    {
        if (!EnsureSupported())
        {
            return;
        }

        var token = TestContext.Current.CancellationToken;
        var parser = CommandApp.Build("test", token);

        var (directory, sourceUri) = CopyMigrations();
        try
        {
            var console = new TestConsole();
            await parser.InvokeAsync(new[]
            {
                "up",
                "--source", sourceUri,
                "--database", _postgres.ConnectionString,
            }, console);

            console = new TestConsole();
            await parser.InvokeAsync(new[]
            {
                "version",
                "--source", sourceUri,
                "--database", _postgres.ConnectionString,
            }, console);

            Assert.Contains("(clean)", console.Out.ToString() ?? string.Empty);

            console = new TestConsole();
            await parser.InvokeAsync(new[]
            {
                "down",
                "1",
                "--source", sourceUri,
                "--database", _postgres.ConnectionString,
            }, console);

            var state = await _postgres.GetStateAsync();
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
        if (!EnsureSupported())
        {
            return;
        }

        var token = TestContext.Current.CancellationToken;
        var parser = CommandApp.Build("test", token);

        var (directory, sourceUri) = CopyMigrations();
        try
        {
            await parser.InvokeAsync(new[]
            {
                "up",
                "--source", sourceUri,
                "--database", _sqlServer.ConnectionString,
            }, new TestConsole());

            await parser.InvokeAsync(new[]
            {
                "drop",
                "--force",
                "--source", sourceUri,
                "--database", _sqlServer.ConnectionString,
            }, new TestConsole());

            var state = await _sqlServer.GetStateAsync();
            Assert.Null(state.Version);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static bool EnsureSupported()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
        {
            return false;
        }
        return true;
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
