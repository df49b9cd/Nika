namespace Nika.Migrations;

public sealed class InMemoryVersionStore : IVersionStore
{
    private readonly object _sync = new();
    private long? _version;
    private bool _isDirty;

    public InMemoryVersionStore(long? version = null, bool isDirty = false)
    {
        if (version.HasValue && version.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version cannot be negative.");
        }

        _version = version;
        _isDirty = isDirty;
    }

    public Task<MigrationVersionState> GetVersionStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            return Task.FromResult(new MigrationVersionState(_version, _isDirty));
        }
    }

    public Task SetVersionStateAsync(long? version, bool isDirty, CancellationToken cancellationToken)
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
}
