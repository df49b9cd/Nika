# Driver & Source Guide

This guide turns Nika’s philosophy into concrete steps for contributors implementing new database drivers or migration sources. Use it alongside [`docs/implementation-philosophy.md`](implementation-philosophy.md) when planning features or reviewing contributions.

## Core Interfaces

Nika’s runtime currently centres on the following abstractions:

```csharp
namespace Nika.Migrations;

public interface IMigrationSource
{
    Task<IReadOnlyCollection<Migration>> LoadMigrationsAsync(CancellationToken cancellationToken);
}

public interface IMigrationDriver
{
    Task ApplyMigrationAsync(Migration migration, CancellationToken cancellationToken);
    Task RevertMigrationAsync(Migration migration, CancellationToken cancellationToken);
}

public interface IScriptMigrationDriver : IMigrationDriver
{
    Task ExecuteScriptAsync(MigrationScript script, CancellationToken cancellationToken);
}

public sealed class Migration
{
    public long Version { get; }
    public string Description { get; }
    public static Migration Create(long version, string description,
        Func<IMigrationDriver, CancellationToken, Task> apply,
        Func<IMigrationDriver, CancellationToken, Task> revert);
}

public sealed class MigrationRunner
{
    public Task UpAsync(CancellationToken cancellationToken = default);
    public Task UpAsync(int maxSteps, CancellationToken cancellationToken = default);
    public Task DownAsync(int maxSteps, CancellationToken cancellationToken = default);
    public Task StepsAsync(int steps, CancellationToken cancellationToken = default);
    public Task ForceAsync(long version, CancellationToken cancellationToken = default);
    public Task<MigrationVersionState> GetVersionStateAsync(CancellationToken cancellationToken = default);
}
```

`MigrationRunner` coordinates sources, drivers, and the `IVersionStore` implementation to ensure versions are applied in order and dirty state is tracked across failures.

## Implementing a Database Driver

- **Initialization** – expose a static factory (e.g. `Postgres.WithConnectionString`) returning the driver, mirroring golang-migrate’s `WithInstance`.
- **Locking** – use database-native primitives (PostgreSQL advisory locks, SQL Server application locks, etc.). The driver should block until it acquires the lock or fail with a timeout.
- **Dirty state handling** – wrap migration execution in `BeginVersionedMigrationAsync`/`CompleteMigrationAsync`. If execution fails, call `MarkDirtyAsync(true)` before surfacing the error.
- **Transactions** – honor `IMigrationRunner` options when transactional DDL is supported. Provide sensible fallbacks (e.g. statement batching) where transactions are unavailable.
- **Streaming execution** – accept `Stream` inputs without buffering entire files in memory. Use sequential pipeline execution to keep resource usage bounded.
- **Configuration** – prefer strongly typed options objects. Avoid hidden global state and environment-variable magic.

### Testing Checklist

- Unit tests for connection handling, locking, and dirty-state persistence.
- Integration tests that run `up`/`down` sequences against each supported version of the database.
- Failure-path tests: corrupted SQL, network interruptions, transaction errors.
- Concurrency tests using multiple runners to verify locking semantics.

### PostgreSQL Driver (Preview)

Nika ships with a lightweight `PostgresScriptMigrationDriver` that executes SQL using [Npgsql](https://www.npgsql.org/). The driver accepts a connection string, optional search path, and transaction toggle. Use it by defining a `driver` block in `nika.config.json`:

```json
{
  "driver": {
    "name": "postgres",
    "connectionStringEnv": "DATABASE_URL",
    "commandTimeoutSeconds": 60,
    "useTransactions": true,
    "searchPath": "public"
  }
}
```

Set `DATABASE_URL` (or inline the connection string with `${ENV_VAR}` interpolation) before running the CLI. The accompanying integration test `PostgresScriptMigrationDriverTests` spins up an ephemeral PostgreSQL instance via `Testcontainers.PostgreSql` (version `4.6.x`), so contributors can exercise the driver without provisioning a local database. Use it as a template for extending coverage with additional soak or concurrency scenarios.

### SQL Server Driver (Preview)

The `SqlServerScriptMigrationDriver` uses [`Microsoft.Data.SqlClient`](https://learn.microsoft.com/sql/connect/ado-net/sqlclient-driver) to execute batches against SQL Server instances. Batches are split on lines containing `GO` (including count repetition, e.g. `GO 3`), mirroring `sqlcmd` semantics. Define the driver as follows:

```json
{
  "driver": {
    "name": "sqlserver",
    "connectionString": "Server=${MSSQL_HOST:-localhost},1433;User Id=sa;Password=${MSSQL_PASSWORD};TrustServerCertificate=True;",
    "commandTimeoutSeconds": 60,
    "useTransactions": true
  }
}
```

Ensure that your connection string sets `TrustServerCertificate=True` (or uses a properly trusted certificate) when targeting development containers. The integration test `SqlServerScriptMigrationDriverTests` provisions an ephemeral SQL Server instance through `Testcontainers.MsSql` (version `4.6.x`) so contributors can validate behaviour without a local installation.

## Implementing a Migration Source

- **Enumeration** – yield migrations sorted by version. Ensure both `up` and `down` variants exist; skip or fail fast when pairs are incomplete.
- **Identifiers** – surface stable URIs (e.g. `file://`, `s3://bucket/key`) for logging and debugging parity with golang-migrate.
- **Streaming** – return delegates that open fresh streams; do not reuse disposed stream instances.
- **Caching** – keep in-process caches optional and bounded. File systems can stream lazily; remote sources may stage downloads in temporary locations when necessary.

### Testing Checklist

- Ordering guarantees validated with mixed version patterns.
- Pair validation tests (missing `down` file, mismatched extensions).
- Large repository enumeration to ensure memory usage remains predictable.
- Remote source authentication and retry behaviors (when applicable).

## Pull Request Expectations

- Include README snippets or dedicated docs describing new drivers/sources and usage.
- Update `docs/driver-and-source-guide.md` with any new shared abstractions or required extension points.
- Provide example usage snippets for both CLI and library contexts.
- Run unit and integration tests locally; attach logs or summaries when raising PRs.

Adhering to these conventions keeps Nika consistent with golang-migrate while offering a polished, idiomatic experience for the .NET ecosystem.

### File System Source (Reference Implementation)

The repository ships with a `FileSystemMigrationSource` that discovers paired migration files using the pattern `&lt;version&gt;_description.up.sql` / `down`. When paired with a driver implementing `IScriptMigrationDriver`, each delegate receives the script content and file path for logging or execution.

Key behaviours:

- Files are grouped by their numeric prefix; duplicates raise `MigrationException`.
- Missing `down` files cause `MigrationRunner.DownAsync` to surface an error so the version store is marked dirty.
- The source streams file contents on demand to avoid keeping large scripts in memory.

See `tests/Nika.Tests/FileSystemMigrationSourceTests.cs` for sample usage and expected guarantees.
