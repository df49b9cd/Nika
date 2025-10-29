namespace Nika.Migrations;

public interface IMigrationDriver : IAsyncDisposable
{
    Task LockAsync(CancellationToken cancellationToken);

    Task UnlockAsync(CancellationToken cancellationToken);

    Task<MigrationVersionState> GetVersionStateAsync(CancellationToken cancellationToken);

    Task SetVersionAsync(long? version, bool isDirty, CancellationToken cancellationToken);

    Task DropAsync(CancellationToken cancellationToken);
}
