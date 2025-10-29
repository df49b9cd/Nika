using System.CommandLine.IO;
using System.CommandLine.Parsing;
using Nika.Cli;
using Xunit;

namespace Nika.Tests;

public class CliCommandTests
{
    [Fact]
    public async Task CreateCommandGeneratesMigrationFiles()
    {
        using var temp = new TemporaryDirectory();

        var parser = CommandApp.Build("test-version", CancellationToken.None);
        var console = new TestConsole();

        var exitCode = await parser.Parse([
            "create",
            "add_users",
            "--dir",
            temp.Path,
            "--ext",
            "sql"
        ]).InvokeAsync(console);

        Assert.Equal(0, exitCode);

        var files = Directory.GetFiles(temp.Path);
        Assert.Equal(2, files.Length);
        Assert.Contains(files, file => file.EndsWith(".up.sql", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, file => file.EndsWith(".down.sql", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(temp.Path, console.Out.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task VersionOptionPrintsVersion()
    {
        var parser = CommandApp.Build("1.2.3", CancellationToken.None);
        var console = new TestConsole();

        var exitCode = await parser.Parse(["--version"]).InvokeAsync(console);

        Assert.Equal(0, exitCode);
        Assert.Contains("1.2.3", console.Out.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task CreateCommandRejectsSeqWithCustomFormat()
    {
        var parser = CommandApp.Build("test", CancellationToken.None);
        var console = new TestConsole();

        var exitCode = await parser.Parse([
            "create",
            "add_users",
            "--ext",
            "sql",
            "--seq",
            "--format",
            "unix"
        ]).InvokeAsync(console);

        Assert.Equal(2, exitCode);
        Assert.Contains("mutually exclusive", console.Error.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task CreateCommandRequiresExtension()
    {
        var parser = CommandApp.Build("test", CancellationToken.None);
        var console = new TestConsole();

        var exitCode = await parser.Parse([
            "create",
            "add_users"
        ]).InvokeAsync(console);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Option '--ext' is required", console.Error.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task StepsCommandRequiresNonZeroArgument()
    {
        var parser = CommandApp.Build("test", CancellationToken.None);
        var console = new TestConsole();

        var exitCode = await parser.Parse([
            "steps",
            "0"
        ]).InvokeAsync(console);

        Assert.Equal(2, exitCode);
        Assert.Contains("must not be zero", console.Error.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task UpCommandRequiresDatabaseOption()
    {
        var parser = CommandApp.Build("test", CancellationToken.None);
        var console = new TestConsole();

        var exitCode = await parser.Parse([
            "up",
            "--source",
            "file:///tmp/migrations"
        ]).InvokeAsync(console);

        Assert.Equal(2, exitCode);
        Assert.Contains("--database option must be provided", console.Error.ToString() ?? string.Empty);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nika-cli-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }
    }
}
