using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nika.Migrations;

public sealed class MigrationRunner(IMigrationSource source, IMigrationDriver driver)
{
    private readonly IMigrationSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IMigrationDriver _driver = driver ?? throw new ArgumentNullException(nameof(driver));
    private readonly SemaphoreSlim _registryLock = new(1, 1);
    private MigrationRegistry? _registry;

    public Task UpAsync(CancellationToken cancellationToken = default)
        => WithDriverLockAsync(ct => UpInternalAsync(limit: null, ct), cancellationToken);

    public Task UpAsync(int maxSteps, CancellationToken cancellationToken = default)
        => maxSteps <= 0
            ? throw new ArgumentOutOfRangeException(nameof(maxSteps), maxSteps, "Max steps must be greater than zero.")
            : WithDriverLockAsync(ct => UpInternalAsync(maxSteps, ct), cancellationToken);

    public Task DownAsync(CancellationToken cancellationToken = default)
        => WithDriverLockAsync(ct => DownInternalAsync(limit: 1, ct), cancellationToken);

    public Task DownAsync(int maxSteps, CancellationToken cancellationToken = default)
        => maxSteps <= 0
            ? throw new ArgumentOutOfRangeException(nameof(maxSteps), maxSteps, "Max steps must be greater than zero.")
            : WithDriverLockAsync(ct => DownInternalAsync(maxSteps, ct), cancellationToken);

    public Task StepsAsync(int steps, CancellationToken cancellationToken = default)
    {
        return steps switch
        {
            > 0 => WithDriverLockAsync(ct => UpInternalAsync(steps, ct), cancellationToken),
            < 0 => WithDriverLockAsync(ct => DownInternalAsync(Math.Abs(steps), ct), cancellationToken),
            _ => Task.CompletedTask,
        };
    }

    public Task ForceAsync(long version, CancellationToken cancellationToken = default)
    {
        if (version < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version cannot be negative.");
        }

        long? targetVersion = version switch
        {
            < 0 => null,
            0 => null,
            _ => version,
        };
        return WithDriverLockAsync(_ => _driver.SetVersionAsync(targetVersion, false, CancellationToken.None), cancellationToken);
    }

    public Task<MigrationVersionState> GetVersionStateAsync(CancellationToken cancellationToken = default)
        => _driver.GetVersionStateAsync(cancellationToken);

    public async Task<long?> VersionAsync(CancellationToken cancellationToken = default)
    {
        var state = await _driver.GetVersionStateAsync(cancellationToken).ConfigureAwait(false);
        return state.Version;
    }

    public Task GotoAsync(long version, CancellationToken cancellationToken = default)
    {
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version cannot be negative.");
        }

        return WithDriverLockAsync(ct => GotoInternalAsync(version, ct), cancellationToken);
    }

    public Task DropAsync(bool force = false, CancellationToken cancellationToken = default)
        => WithDriverLockAsync(ct => DropInternalAsync(force, ct), cancellationToken);

    private async Task UpInternalAsync(int? limit, CancellationToken cancellationToken)
    {
        var registry = await GetRegistryAsync(cancellationToken).ConfigureAwait(false);
        var state = await _driver.GetVersionStateAsync(cancellationToken).ConfigureAwait(false);

        if (state.IsDirty)
        {
            throw new DirtyMigrationStateException();
        }

        var pending = registry.GetMigrationsAfter(state.Version, limit);
        foreach (var migration in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _driver.SetVersionAsync(migration.Version, true, CancellationToken.None).ConfigureAwait(false);

            try
            {
                await migration.ApplyAsync(_driver, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await _driver.SetVersionAsync(migration.Version, true, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await _driver.SetVersionAsync(migration.Version, true, CancellationToken.None).ConfigureAwait(false);
                throw new MigrationException($"Failed to apply migration {migration.Version}: {migration.Description}", ex);
            }

            await _driver.SetVersionAsync(migration.Version, false, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task DownInternalAsync(int? limit, CancellationToken cancellationToken)
    {
        var registry = await GetRegistryAsync(cancellationToken).ConfigureAwait(false);
        var state = await _driver.GetVersionStateAsync(cancellationToken).ConfigureAwait(false);

        if (state.IsDirty)
        {
            throw new DirtyMigrationStateException();
        }

        if (state.Version is null)
        {
            return;
        }

        if (!registry.TryGetMigration(state.Version.Value, out _))
        {
            throw new MissingMigrationException(state.Version.Value);
        }

        var toRevert = registry.GetMigrationsAtOrBelow(state.Version.Value, limit);
        if (toRevert.Count == 0)
        {
            throw new MissingMigrationException(state.Version.Value);
        }

        foreach (var migration in toRevert)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _driver.SetVersionAsync(migration.Version, true, CancellationToken.None).ConfigureAwait(false);

            try
            {
                await migration.RevertAsync(_driver, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await _driver.SetVersionAsync(migration.Version, true, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await _driver.SetVersionAsync(migration.Version, true, CancellationToken.None).ConfigureAwait(false);
                throw new MigrationException($"Failed to revert migration {migration.Version}: {migration.Description}", ex);
            }

            var previousVersion = registry.GetPreviousVersion(migration.Version);
            await _driver.SetVersionAsync(previousVersion, false, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task GotoInternalAsync(long version, CancellationToken cancellationToken)
    {
        var registry = await GetRegistryAsync(cancellationToken).ConfigureAwait(false);
        var state = await _driver.GetVersionStateAsync(cancellationToken).ConfigureAwait(false);

        if (state.IsDirty)
        {
            throw new DirtyMigrationStateException();
        }

        var currentVersion = state.Version;
        if (currentVersion == version)
        {
            return;
        }

        if (currentVersion is null || currentVersion.Value < version)
        {
            var steps = CountMigrationsToAdvance(registry, currentVersion, version);
            if (steps > 0)
            {
                await UpInternalAsync(steps, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            var steps = CountMigrationsToRevert(registry, currentVersion.Value, version);
            if (steps > 0)
            {
                await DownInternalAsync(steps, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DropInternalAsync(bool force, CancellationToken cancellationToken)
    {
        var state = await _driver.GetVersionStateAsync(cancellationToken).ConfigureAwait(false);

        if (state.IsDirty && !force)
        {
            throw new DirtyMigrationStateException();
        }

        if (state.IsDirty)
        {
            await _driver.SetVersionAsync(state.Version, false, CancellationToken.None).ConfigureAwait(false);
        }

        await _driver.DropAsync(cancellationToken).ConfigureAwait(false);
        await _driver.SetVersionAsync(null, false, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<MigrationRegistry> GetRegistryAsync(CancellationToken cancellationToken)
    {
        if (_registry is not null)
        {
            return _registry;
        }

        await _registryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_registry is null)
            {
                var migrations = await _source.LoadMigrationsAsync(cancellationToken).ConfigureAwait(false);
                _registry = new MigrationRegistry(migrations);
            }
        }
        finally
        {
            _registryLock.Release();
        }

        return _registry;
    }

    private static int CountMigrationsToAdvance(MigrationRegistry registry, long? currentVersion, long targetVersion)
    {
        return registry.CountMigrationsBetween(currentVersion, targetVersion);
    }

    private static int CountMigrationsToRevert(MigrationRegistry registry, long currentVersion, long targetVersion)
    {
        return registry.CountMigrationsBetween(targetVersion, currentVersion);
    }

    private async Task WithDriverLockAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await _driver.LockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _driver.UnlockAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
