using System;
using System.Threading;
using System.Threading.Tasks;
using Nika.Migrations;
using Xunit;

namespace Nika.Tests;

public class MigrationRunnerTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UpAsync_AppliesPendingMigrationsInOrder()
    {
        var source = BuildSequentialSource(1, 3);
        await using var driver = new InMemoryMigrationDriver();
        var runner = MigrationEngine.New(source, driver);

        await runner.UpAsync(TestToken);

        Assert.Equal([1, 2, 3], driver.AppliedVersions);
        var state = await driver.GetVersionStateAsync(TestToken);
        Assert.Equal(3, state.Version);
        Assert.False(state.IsDirty);
    }

    [Fact]
    public async Task DownAsync_RevertsLatestMigration()
    {
        var source = BuildSequentialSource(1, 3);
        await using var driver = new InMemoryMigrationDriver();
        var runner = MigrationEngine.New(source, driver);

        await runner.UpAsync(TestToken);
        await runner.DownAsync(TestToken);

        Assert.Equal([3], driver.RevertedVersions);
        var state = await driver.GetVersionStateAsync(TestToken);
        Assert.Equal(2, state.Version);
        Assert.False(state.IsDirty);
    }

    [Fact]
    public async Task StepsAsync_WithPositiveValueAppliesSpecifiedNumber()
    {
        var source = BuildSequentialSource(1, 4);
        await using var driver = new InMemoryMigrationDriver();
        var runner = MigrationEngine.New(source, driver);

        await runner.StepsAsync(2, TestToken);

        Assert.Equal([1, 2], driver.AppliedVersions);
        var state = await driver.GetVersionStateAsync(TestToken);
        Assert.Equal(2, state.Version);
        Assert.False(state.IsDirty);
    }

    [Fact]
    public async Task StepsAsync_WithNegativeValueRevertsSpecifiedNumber()
    {
        var source = BuildSequentialSource(1, 4);
        await using var driver = new InMemoryMigrationDriver();
        var runner = MigrationEngine.New(source, driver);

        await runner.UpAsync(TestToken);
        await runner.StepsAsync(-2, TestToken);

        Assert.Equal([4, 3], driver.RevertedVersions);
        var state = await driver.GetVersionStateAsync(TestToken);
        Assert.Equal(2, state.Version);
        Assert.False(state.IsDirty);
    }

    [Fact]
    public async Task ForceAsync_SetsVersionAndClearsDirty()
    {
        var source = BuildSequentialSource(1, 2);
        await using var driver = new InMemoryMigrationDriver();
        var runner = MigrationEngine.New(source, driver);

        await runner.ForceAsync(5, TestToken);

        var state = await driver.GetVersionStateAsync(TestToken);
        Assert.Equal(5, state.Version);
        Assert.False(state.IsDirty);
    }

    [Fact]
    public async Task UpAsync_WhenDirty_Throws()
    {
        var source = BuildSequentialSource(1, 2);
        await using var driver = new InMemoryMigrationDriver();
        var runner = MigrationEngine.New(source, driver);

        await driver.SetVersionAsync(1, true, TestToken);

        await Assert.ThrowsAsync<DirtyMigrationStateException>(() => runner.UpAsync(TestToken));
    }

    [Fact]
    public async Task DownAsync_WithNoCurrentVersion_DoesNothing()
    {
        var source = BuildSequentialSource(1, 2);
        await using var driver = new InMemoryMigrationDriver();
        var runner = MigrationEngine.New(source, driver);

        await runner.DownAsync(TestToken);

        Assert.Empty(driver.RevertedVersions);
        var state = await driver.GetVersionStateAsync(TestToken);
        Assert.Null(state.Version);
        Assert.False(state.IsDirty);
    }

    [Fact]
    public async Task DownAsync_WhenMigrationMissing_Throws()
    {
        var source = BuildSequentialSource(1, 2);
        await using var driver = new InMemoryMigrationDriver();
        var runner = MigrationEngine.New(source, driver);

        await driver.SetVersionAsync(5, false, TestToken);

        var ex = await Assert.ThrowsAsync<MissingMigrationException>(() => runner.DownAsync(TestToken));
        Assert.Equal(5, ex.Version);

        var state = await driver.GetVersionStateAsync(TestToken);
        Assert.Equal(5, state.Version);
        Assert.False(state.IsDirty);
    }

    [Fact]
    public async Task UpAsync_WhenMigrationFails_MarksDirtyAndThrows()
    {
        await using var driver = new InMemoryMigrationDriver();
        var source = new InMemoryMigrationSource()
            .AddMigration(Migration.Create(
                1,
                "ok",
                (d, _) =>
                {
                    var inMemoryDriver = (InMemoryMigrationDriver)d;
                    inMemoryDriver.AppendLog("up-1");
                    inMemoryDriver.RecordApply(1);
                    return Task.CompletedTask;
                },
                (d, _) => Task.CompletedTask))
            .AddMigration(Migration.Create(
                2,
                "boom",
                (_, _) => throw new InvalidOperationException("fail"),
                (d, _) => Task.CompletedTask));

        var runner = MigrationEngine.New(source, driver);

        var ex = await Assert.ThrowsAsync<MigrationException>(() => runner.UpAsync(TestToken));
        Assert.Contains("Failed to apply migration 2", ex.Message);

        Assert.Equal([1], driver.AppliedVersions);
        var state = await driver.GetVersionStateAsync(TestToken);
        Assert.Equal(2, state.Version);
        Assert.True(state.IsDirty);

        await Assert.ThrowsAsync<DirtyMigrationStateException>(() => runner.UpAsync(TestToken));
    }

    [Fact]
    public async Task DownAsync_WhenMigrationFails_MarksDirtyAndThrows()
    {
        await using var driver = new InMemoryMigrationDriver();
        var source = new InMemoryMigrationSource()
            .AddMigration(Migration.Create(
                1,
                "ok",
                (d, _) =>
                {
                    var inMemoryDriver = (InMemoryMigrationDriver)d;
                    inMemoryDriver.AppendLog("up-1");
                    inMemoryDriver.RecordApply(1);
                    return Task.CompletedTask;
                },
                (d, _) =>
                {
                    var inMemoryDriver = (InMemoryMigrationDriver)d;
                    inMemoryDriver.RecordRevert(1);
                    return Task.CompletedTask;
                }))
            .AddMigration(Migration.Create(
                2,
                "down fails",
                (d, _) =>
                {
                    var inMemoryDriver = (InMemoryMigrationDriver)d;
                    inMemoryDriver.AppendLog("up-2");
                    inMemoryDriver.RecordApply(2);
                    return Task.CompletedTask;
                },
                (_, _) => throw new InvalidOperationException("fail down")));

        var runner = MigrationEngine.New(source, driver);
        await runner.UpAsync(TestToken);

        var ex = await Assert.ThrowsAsync<MigrationException>(() => runner.DownAsync(TestToken));
        Assert.Contains("Failed to revert migration 2", ex.Message);

        var state = await driver.GetVersionStateAsync(TestToken);
        Assert.Equal(2, state.Version);
        Assert.True(state.IsDirty);
    }

    [Fact]
    public async Task StepsAsync_WithZeroDoesNothing()
    {
        var source = BuildSequentialSource(1, 3);
        await using var driver = new InMemoryMigrationDriver();
        var runner = MigrationEngine.New(source, driver);

        await runner.StepsAsync(0, TestToken);

        Assert.Empty(driver.AppliedVersions);
        var state = await driver.GetVersionStateAsync(TestToken);
        Assert.Null(state.Version);
    }

    [Fact]
    public async Task UpAsync_HonorsCancellationDuringExecution()
    {
        await using var driver = new InMemoryMigrationDriver();
        using var cts = new CancellationTokenSource();

        var source = new InMemoryMigrationSource()
            .AddMigration(Migration.Create(
                1,
                "fast",
                (d, _) =>
                {
                    var inMemoryDriver = (InMemoryMigrationDriver)d;
                    inMemoryDriver.AppendLog("up-1");
                    inMemoryDriver.RecordApply(1);
                    return Task.CompletedTask;
                },
                (d, _) => Task.CompletedTask))
            .AddMigration(Migration.Create(
                2,
                "slow",
                async (_, ct) =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                },
                (d, _) => Task.CompletedTask));

        var runner = MigrationEngine.New(source, driver);
        var upTask = runner.UpAsync(cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(50), TestToken);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => upTask);

        Assert.Equal([1], driver.AppliedVersions);
        var state = await driver.GetVersionStateAsync(TestToken);
        Assert.Equal(2, state.Version);
        Assert.True(state.IsDirty);
    }

    private static InMemoryMigrationSource BuildSequentialSource(long start, long endInclusive)
    {
        var source = new InMemoryMigrationSource();
        for (var version = start; version <= endInclusive; version++)
        {
            var currentVersion = version;
            source.AddMigration(Migration.Create(
                currentVersion,
                $"migration-{currentVersion}",
                (driver, _) =>
                {
                    var inMemoryDriver = (InMemoryMigrationDriver)driver;
                    inMemoryDriver.AppendLog($"up-{currentVersion}");
                    inMemoryDriver.RecordApply(currentVersion);
                    return Task.CompletedTask;
                },
                (driver, _) =>
                {
                    var inMemoryDriver = (InMemoryMigrationDriver)driver;
                    inMemoryDriver.AppendLog($"down-{currentVersion}");
                    inMemoryDriver.RecordRevert(currentVersion);
                    return Task.CompletedTask;
                }));
        }

        return source;
    }
}
