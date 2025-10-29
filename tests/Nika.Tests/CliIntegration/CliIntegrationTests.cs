using System;
using System.CommandLine.IO;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Nika.Cli;
using Testcontainers.PostgreSql;
using Testcontainers.MsSql;
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
        SkipIfUnsupported();

        var migrations = CopyMigrations();
        var parser = CommandApp.Build("test", default);
        var console = new TestConsole();

        await parser.Parse(new[]
        {
            "up",
            "--source", $"file://{migrations}",
            "--database", _postgres.ConnectionString,
        }).InvokeAsync(console);

        console = new TestConsole();
        await parser.Parse(new[]
        {
            "version",
            "--source", $"file://{migrations}",
            "--database", _postgres.ConnectionString,
        }).InvokeAsync(console);

        Assert.Contains("(clean)", console.Out.ToString() ?? string.Empty);

        console = new TestConsole();
        await parser.Parse(new[]
        {
            "down",
            "1",
            "--source", $"file://{migrations}",
            "--database", _postgres.ConnectionString,
        }).InvokeAsync(console);

        var state = await _postgres.GetStateAsync();
        Assert.Equal(2, state.Version);
    }

    [Fact]
    public async Task SqlServer_DropForce()
    {
        SkipIfUnsupported();

        var migrations = CopyMigrations();
        var parser = CommandApp.Build("test", default);

        await parser.Parse(new[]
        {
            "up",
            "--source", $"file://{migrations}",
            "--database", _sqlServer.ConnectionString,
        }).InvokeAsync(new TestConsole());

        await parser.Parse(new[]
        {
            "drop",
            "--force",
            "--source", $"file://{migrations}",
            "--database", _sqlServer.ConnectionString,
        }).InvokeAsync(new TestConsole());

        var state = await _sqlServer.GetStateAsync();
        Assert.Null(state.Version);
    }

    private static void SkipIfUnsupported()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new SkipException("Testcontainers-based CLI tests require Linux environment.");
        }

        if (Environment.GetEnvironmentVariable("CI") is null)
        {
            throw new SkipException("Skipping container-backed CLI integration tests outside CI runner.");
        }
    }

    private static string CopyMigrations()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var examples = Path.Combine(sourceRoot, "examples", "migrations");
        var target = Path.Combine(Path.GetTempPath(), "nika-cli-it", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(examples))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)!));
        }

        return target;
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
        await using var driver = new Nika.Migrations.Drivers.Postgres.PostgresScriptMigrationDriver(
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
        await using var driver = new Nika.Migrations.Drivers.SqlServer.SqlServerScriptMigrationDriver(
            new SqlServerScriptMigrationDriverOptions(ConnectionString));
        return await driver.GetVersionStateAsync(default);
    }
}
