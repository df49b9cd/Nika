namespace Nika.Migrations;

public class MigrationException : Exception
{
    public MigrationException(string message)
        : base(message)
    {
    }

    public MigrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class DirtyMigrationStateException()
    : MigrationException("Cannot perform migration operations while the version store is marked dirty.");

public sealed class MissingMigrationException(long version) : MigrationException($"Migration with version '{version}' was not found in the registry.")
{
    public long Version { get; } = version;
}
