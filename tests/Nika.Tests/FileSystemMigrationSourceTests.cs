using Nika.Migrations;
using Nika.Migrations.Sources;
using Xunit;

namespace Nika.Tests;

public sealed class FileSystemMigrationSourceTests : IDisposable
{
    private readonly string _directory;
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public FileSystemMigrationSourceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "nika-fs-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task LoadsMigrationsFromFilesystemAndExecutesScripts()
    {
        var up1 = Path.Combine(_directory, "0001_create_table.up.sql");
        var down1 = Path.Combine(_directory, "0001_create_table.down.sql");
        var up2 = Path.Combine(_directory, "0002_seed_data.up.sql");
        var down2 = Path.Combine(_directory, "0002_seed_data.down.sql");

        await File.WriteAllTextAsync(up1, "CREATE TABLE items();", TestToken);
        await File.WriteAllTextAsync(down1, "DROP TABLE items;", TestToken);
        await File.WriteAllTextAsync(up2, "INSERT INTO items VALUES (1);", TestToken);
        await File.WriteAllTextAsync(down2, "DELETE FROM items;", TestToken);

        var source = new FileSystemMigrationSource(_directory);
        await using var driver = new RecordingScriptDriver();
        var runner = MigrationEngine.New(source, driver);

        await runner.UpAsync(TestToken);
        Assert.Equal(new long[] { 1, 2 }, driver.AppliedVersions);
        Assert.Collection(
            driver.Scripts,
            script =>
            {
                Assert.Equal(1, script.Version);
                Assert.Equal(MigrationDirection.Up, script.Direction);
                Assert.Equal("CREATE TABLE items();", script.Content);
            },
            script =>
            {
                Assert.Equal(2, script.Version);
                Assert.Equal(MigrationDirection.Up, script.Direction);
                Assert.Equal("INSERT INTO items VALUES (1);", script.Content);
            });

        await runner.DownAsync(TestToken);
        Assert.Equal(new long[] { 2 }, driver.RevertedVersions);
        var lastScript = driver.Scripts[^1];
        Assert.Equal(MigrationDirection.Down, lastScript.Direction);
        Assert.Equal("DELETE FROM items;", lastScript.Content);
    }

    [Fact]
    public async Task MissingDownMigrationThrowsDuringRevert()
    {
        var up1 = Path.Combine(_directory, "0001_create_table.up.sql");
        await File.WriteAllTextAsync(up1, "CREATE TABLE items();", TestToken);

        var source = new FileSystemMigrationSource(_directory);
        await using var driver = new RecordingScriptDriver();
        var runner = MigrationEngine.New(source, driver);

        await runner.UpAsync(TestToken);

        await Assert.ThrowsAsync<MigrationException>(() => runner.DownAsync(TestToken));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private sealed class RecordingScriptDriver : IScriptMigrationDriver
    {
        private readonly List<long> _applied = [];
        private readonly List<long> _reverted = [];
        private readonly List<(long Version, MigrationDirection Direction, string Content)> _scripts = [];
        private long? _version;
        private bool _isDirty;
        private bool _locked;

        public string? LastScriptContent { get; private set; }

        public long[] AppliedVersions => _applied.ToArray();

        public long[] RevertedVersions => _reverted.ToArray();

        public IReadOnlyList<(long Version, MigrationDirection Direction, string Content)> Scripts => _scripts;

        public Task LockAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_locked)
            {
                throw new MigrationException("Driver already locked.");
            }

            _locked = true;
            return Task.CompletedTask;
        }

        public Task UnlockAsync(CancellationToken cancellationToken)
        {
            _locked = false;
            return Task.CompletedTask;
        }

        public Task<MigrationVersionState> GetVersionStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new MigrationVersionState(_version, _isDirty));
        }

        public Task SetVersionAsync(long? version, bool isDirty, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _version = version;
            _isDirty = isDirty;
            return Task.CompletedTask;
        }

        public Task DropAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _version = null;
            _isDirty = false;
            _applied.Clear();
            _reverted.Clear();
            _scripts.Clear();
            LastScriptContent = null;
            return Task.CompletedTask;
        }

        public async Task ExecuteScriptAsync(MigrationScript script, CancellationToken cancellationToken)
        {
            var content = await script.GetContentAsync(cancellationToken).ConfigureAwait(false);
            LastScriptContent = content;
            _scripts.Add((script.Version, script.Direction, content));

            if (script.Direction == MigrationDirection.Up)
            {
                _applied.Add(script.Version);
            }
            else
            {
                _reverted.Add(script.Version);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
