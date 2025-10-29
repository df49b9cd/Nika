using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Linq;
using Microsoft.Data.SqlClient;
using Nika.Cli;
using Nika.Migrations;
using Nika.Migrations.Drivers.Postgres;
using Nika.Migrations.Drivers.SqlServer;
using Npgsql;
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

        var copy = CopyMigrations("Postgres/examples/migrations");
        var directory = copy.Directory;
        var sourceUri = copy.SourceUri;
        var versions = copy.Versions;
        var databaseUri = BuildPostgresDatabaseUri(postgres.ConnectionString);
        try
        {
            var console = new TestConsole();
            var exitCode = await parser.InvokeAsync([
                "up",
                "--source", sourceUri,
                "--database", databaseUri
            ], console);
            Assert.True(exitCode == 0, console.Error.ToString());
            Assert.True(string.IsNullOrEmpty(console.Error.ToString()), console.Error.ToString());

            console = new TestConsole();
            exitCode = await parser.InvokeAsync([
                "version",
                "--source", sourceUri,
                "--database", databaseUri
            ], console);
            Assert.True(exitCode == 0, console.Error.ToString());
            Assert.True(string.IsNullOrEmpty(console.Error.ToString()), console.Error.ToString());

            Assert.Contains("(clean)", console.Out.ToString() ?? string.Empty);

            console = new TestConsole();
            exitCode = await parser.InvokeAsync([
                "down",
                "1",
                "--source", sourceUri,
                "--database", databaseUri
            ], console);
            Assert.True(exitCode == 0, console.Error.ToString());
            Assert.True(string.IsNullOrEmpty(console.Error.ToString()), console.Error.ToString());

            var expectedVersion = versions.Count > 1 ? versions[^2] : (long?)null;
            var state = await postgres.GetStateAsync();
            Assert.Equal(expectedVersion, state.Version);
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

        var copy = CopyMigrations("SqlServer/examples/migrations");
        var directory = copy.Directory;
        var sourceUri = copy.SourceUri;
        var databaseUri = BuildSqlServerDatabaseUri(sqlServer.ConnectionString);
        try
        {
            var console = new TestConsole();
            var exitCode = await parser.InvokeAsync([
                "up",
                "--source", sourceUri,
                "--database", databaseUri
            ], console);
            Assert.True(exitCode == 0, console.Error.ToString());
            Assert.True(string.IsNullOrEmpty(console.Error.ToString()), console.Error.ToString());

            console = new TestConsole();
            exitCode = await parser.InvokeAsync([
                "drop",
                "--force",
                "--source", sourceUri,
                "--database", databaseUri
            ], console);
            Assert.True(exitCode == 0, console.Error.ToString());
            Assert.True(string.IsNullOrEmpty(console.Error.ToString()), console.Error.ToString());

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

    private static MigrationCopy CopyMigrations(string relativeSourceDirectory)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sanitizedRelativePath = relativeSourceDirectory
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var examples = Path.Combine(
            repositoryRoot,
            "tests",
            "Nika.Tests",
            sanitizedRelativePath);
        var target = Path.Combine(Path.GetTempPath(), "nika-cli-it", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);

        var descriptors = new Dictionary<long, MigrationFiles>();
        foreach (var file in Directory.GetFiles(examples))
        {
            var fileName = Path.GetFileName(file);
            if (fileName is null)
            {
                continue;
            }

            string? direction = null;
            if (fileName.EndsWith(".up.sql", StringComparison.OrdinalIgnoreCase))
            {
                direction = "up";
            }
            else if (fileName.EndsWith(".down.sql", StringComparison.OrdinalIgnoreCase))
            {
                direction = "down";
            }
            else
            {
                continue;
            }

            var suffixLength = direction == "up" ? ".up.sql".Length : ".down.sql".Length;
            var baseName = fileName[..^suffixLength];
            var underscoreIndex = baseName.IndexOf('_');
            var versionToken = underscoreIndex >= 0 ? baseName[..underscoreIndex] : baseName;

            if (!long.TryParse(versionToken, out var version))
            {
                continue;
            }

            if (!descriptors.TryGetValue(version, out var descriptor))
            {
                descriptor = new MigrationFiles();
                descriptors[version] = descriptor;
            }

            switch (direction)
            {
                case "up" when descriptor.UpPath is null:
                    descriptor.UpPath = file;
                    break;
                case "down" when descriptor.DownPath is null:
                    descriptor.DownPath = file;
                    break;
            }
        }

        var selectedVersions = descriptors
            .Where(pair => pair.Value.UpPath is not null && pair.Value.DownPath is not null)
            .Select(pair => pair.Key)
            .OrderBy(version => version)
            .ToList();

        foreach (var version in selectedVersions)
        {
            var descriptor = descriptors[version];
            CopyFile(descriptor.UpPath!, target);
            CopyFile(descriptor.DownPath!, target);
        }

        var uri = new Uri(Path.EndsInDirectorySeparator(target) ? target : target + Path.DirectorySeparatorChar).AbsoluteUri;
        return new MigrationCopy(target, uri, selectedVersions);
    }

    private static void CopyFile(string sourcePath, string targetDirectory)
    {
        var fileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrEmpty(fileName))
        {
            return;
        }

        var targetPath = Path.Combine(targetDirectory, fileName);
        File.Copy(sourcePath, targetPath, overwrite: false);
    }

    private static string BuildPostgresDatabaseUri(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var host = string.IsNullOrWhiteSpace(builder.Host) ? "localhost" : builder.Host;
        var uriBuilder = new UriBuilder("postgres", host)
        {
            Path = "/" + Uri.EscapeDataString(builder.Database ?? string.Empty),
        };

        if (builder.Port > 0)
        {
            uriBuilder.Port = builder.Port;
        }

        if (!string.IsNullOrWhiteSpace(builder.Username))
        {
            uriBuilder.UserName = builder.Username;
        }

        if (!string.IsNullOrWhiteSpace(builder.Password))
        {
            uriBuilder.Password = builder.Password;
        }

        return uriBuilder.Uri.ToString();
    }

    private static string BuildSqlServerDatabaseUri(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);

        var dataSource = builder.DataSource?.Trim() ?? string.Empty;
        if (dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            dataSource = dataSource[4..];
        }

        var host = dataSource;
        var port = -1;
        var commaIndex = dataSource.IndexOf(',');
        if (commaIndex >= 0 && commaIndex < dataSource.Length - 1)
        {
            host = dataSource[..commaIndex];
            var portPart = dataSource[(commaIndex + 1)..];
            _ = int.TryParse(portPart, out port);
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            host = "localhost";
        }

        var uriBuilder = new UriBuilder("sqlserver", host)
        {
            Path = "/" + Uri.EscapeDataString(builder.InitialCatalog ?? string.Empty),
        };

        if (port > 0)
        {
            uriBuilder.Port = port;
        }

        if (!string.IsNullOrWhiteSpace(builder.UserID))
        {
            uriBuilder.UserName = builder.UserID;
        }

        if (!string.IsNullOrWhiteSpace(builder.Password))
        {
            uriBuilder.Password = builder.Password;
        }

        var queryParameters = new List<string>();
        if (builder.TrustServerCertificate)
        {
            queryParameters.Add("TrustServerCertificate=True");
        }

        if (builder.Encrypt)
        {
            queryParameters.Add("Encrypt=True");
        }

        if (queryParameters.Count > 0)
        {
            uriBuilder.Query = string.Join("&", queryParameters);
        }

        return uriBuilder.Uri.ToString();
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

    private sealed record MigrationCopy(string Directory, string SourceUri, IReadOnlyList<long> Versions);

    private sealed class MigrationFiles
    {
        public string? UpPath { get; set; }

        public string? DownPath { get; set; }
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
