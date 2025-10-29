using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nika.Migrations;

public sealed class InMemoryMigrationDriver : IMigrationDriver
{
    private readonly object _sync = new();
    private readonly List<long> _appliedVersions = [];
    private readonly List<long> _revertedVersions = [];
    private readonly List<string> _log = [];
    private bool _locked;
    private long? _version;
    private bool _isDirty;

    public IReadOnlyList<long> AppliedVersions => _appliedVersions;

    public IReadOnlyList<long> RevertedVersions => _revertedVersions;

    public IReadOnlyList<string> Log => _log;

    public void RecordApply(long version)
    {
        lock (_sync)
        {
            _appliedVersions.Add(version);
        }
    }

    public void RecordRevert(long version)
    {
        lock (_sync)
        {
            _revertedVersions.Add(version);
        }
    }

    public void AppendLog(string entry)
    {
        lock (_sync)
        {
            _log.Add(entry);
        }
    }

    public Task LockAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_locked)
            {
                throw new MigrationException("Driver is already locked.");
            }

            _locked = true;
        }

        return Task.CompletedTask;
    }

    public Task UnlockAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _locked = false;
        }

        return Task.CompletedTask;
    }

    public Task<MigrationVersionState> GetVersionStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            return Task.FromResult(new MigrationVersionState(_version, _isDirty));
        }
    }

    public Task SetVersionAsync(long? version, bool isDirty, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (version.HasValue && version.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version cannot be negative.");
        }

        lock (_sync)
        {
            _version = version;
            _isDirty = isDirty;
        }

        return Task.CompletedTask;
    }

    public Task DropAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _version = null;
            _isDirty = false;
            _appliedVersions.Clear();
            _revertedVersions.Clear();
            _log.Clear();
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}
