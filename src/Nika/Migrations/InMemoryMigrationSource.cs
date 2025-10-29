using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nika.Migrations;

public sealed class InMemoryMigrationSource : IMigrationSource
{
    private readonly List<Migration> _migrations;

    public InMemoryMigrationSource()
        : this(Array.Empty<Migration>())
    {
    }

    public InMemoryMigrationSource(IEnumerable<Migration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        _migrations = new List<Migration>(migrations);
    }

    public InMemoryMigrationSource AddMigration(Migration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        _migrations.Add(migration);
        return this;
    }

    public InMemoryMigrationSource AddMigration(
        long version,
        string description,
        Func<IMigrationDriver, CancellationToken, Task> applyAsync,
        Func<IMigrationDriver, CancellationToken, Task> revertAsync)
    {
        _migrations.Add(Migration.Create(version, description, applyAsync, revertAsync));
        return this;
    }

    public Task<IReadOnlyCollection<Migration>> LoadMigrationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyCollection<Migration>>(_migrations.ToArray());
    }
}
