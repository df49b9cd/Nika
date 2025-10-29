namespace Nika.Migrations;

internal sealed class MigrationRegistry
{
    private readonly List<Migration> _ordered;
    private readonly Dictionary<long, int> _indexByVersion;

    public MigrationRegistry(IEnumerable<Migration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        _ordered = migrations.OrderBy(m => m.Version).ToList();
        _indexByVersion = new Dictionary<long, int>(_ordered.Count);

        for (var i = 0; i < _ordered.Count; i++)
        {
            var migration = _ordered[i];
            if (_indexByVersion.ContainsKey(migration.Version))
            {
                throw new MigrationException($"Duplicate migration version detected: {migration.Version}.");
            }

            _indexByVersion[migration.Version] = i;
        }
    }

    public IReadOnlyList<Migration> Ordered => _ordered;

    public bool TryGetMigration(long version, out Migration migration)
    {
        if (_indexByVersion.TryGetValue(version, out var index))
        {
            migration = _ordered[index];
            return true;
        }

        migration = null!;
        return false;
    }

    public IReadOnlyList<Migration> GetMigrationsAfter(long? version, int? limit)
    {
        var results = new List<Migration>();

        var startIndex = 0;
        if (version.HasValue)
        {
            while (startIndex < _ordered.Count && _ordered[startIndex].Version <= version.Value)
            {
                startIndex++;
            }
        }

        for (var i = startIndex; i < _ordered.Count; i++)
        {
            if (limit.HasValue && results.Count >= limit.Value)
            {
                break;
            }

            results.Add(_ordered[i]);
        }

        return results;
    }

    public IReadOnlyList<Migration> GetMigrationsAtOrBelow(long version, int? limit)
    {
        var results = new List<Migration>();

        if (_ordered.Count == 0)
        {
            return results;
        }

        var startIndex = _ordered.Count - 1;
        while (startIndex >= 0 && _ordered[startIndex].Version > version)
        {
            startIndex--;
        }

        for (var i = startIndex; i >= 0; i--)
        {
            if (limit.HasValue && results.Count >= limit.Value)
            {
                break;
            }

            results.Add(_ordered[i]);
        }

        return results;
    }

    public long? GetPreviousVersion(long version)
    {
        if (!_indexByVersion.TryGetValue(version, out var index))
        {
            return null;
        }

        if (index == 0)
        {
            return null;
        }

        return _ordered[index - 1].Version;
    }

    public int CountMigrationsBetween(long? lowerExclusive, long upperInclusive)
    {
        var count = 0;

        foreach (var migration in _ordered)
        {
            if (migration.Version > upperInclusive)
            {
                break;
            }

            var isAboveLowerBound = !lowerExclusive.HasValue || migration.Version > lowerExclusive.Value;
            if (isAboveLowerBound && migration.Version <= upperInclusive)
            {
                count++;
            }
        }

        return count;
    }
}
