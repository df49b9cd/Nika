namespace Nika.Migrations;

public interface IVersionStore
{
    Task<MigrationVersionState> GetVersionStateAsync(CancellationToken cancellationToken);

    Task SetVersionStateAsync(long? version, bool isDirty, CancellationToken cancellationToken);
}

public sealed record MigrationVersionState(long? Version, bool IsDirty);
