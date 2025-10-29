using System;

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

public sealed class DirtyMigrationStateException : MigrationException
{
    public DirtyMigrationStateException()
        : base("Cannot perform migration operations while the version store is marked dirty.")
    {
    }
}

public sealed class MissingMigrationException : MigrationException
{
    public MissingMigrationException(long version)
        : base($"Migration with version '{version}' was not found in the registry.")
    {
        Version = version;
    }

    public long Version { get; }
}
