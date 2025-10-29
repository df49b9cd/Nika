namespace Nika.Migrations;

public interface IMigrationSource
{
    Task<IReadOnlyCollection<Migration>> LoadMigrationsAsync(CancellationToken cancellationToken);
}
