using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nika.Migrations;
using Xunit;

namespace Nika.Tests;

public class FileVersionStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly CancellationToken _token = CancellationToken.None;

    public FileVersionStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "nika-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task ReturnsDefaultWhenStateMissing()
    {
        var path = Path.Combine(_directory, "state.json");
        var store = new FileVersionStore(path);

        var state = await store.GetVersionStateAsync(_token);

        Assert.Null(state.Version);
        Assert.False(state.IsDirty);
    }

    [Fact]
    public async Task PersistsStateBetweenInstances()
    {
        var path = Path.Combine(_directory, "state.json");
        var store = new FileVersionStore(path);

        await store.SetVersionStateAsync(12, true, _token);

        var reloaded = new FileVersionStore(path);
        var state = await reloaded.GetVersionStateAsync(_token);

        Assert.Equal(12, state.Version);
        Assert.True(state.IsDirty);
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
            // ignore cleanup errors in tests
        }
    }
}
