using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nika.Migrations;
using Npgsql;

namespace Nika.Migrations.Drivers.Postgres;

public sealed class PostgresScriptMigrationDriver(PostgresScriptMigrationDriverOptions options) : IScriptMigrationDriver
{
    private const long NilVersion = -1;

    private readonly PostgresScriptMigrationDriverOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    private NpgsqlConnection? _connection;
    private bool _lockHeld;
    private long _advisoryLockKey;
    private bool _disposed;
    private string? _migrationsSchema;
    private string? _migrationsTable;
    private string? _qualifiedMigrationsTable;

    public async Task LockAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken, acquireLock: true).ConfigureAwait(false);
        await EnsureMigrationsTableAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnlockAsync(CancellationToken cancellationToken)
    {
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is null)
            {
                _lockHeld = false;
                return;
            }

            await ReleaseAdvisoryLockAsync(_connection, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<MigrationVersionState> GetVersionStateAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken, acquireLock: false).ConfigureAwait(false);
        await EnsureMigrationsTableAsync(connection, cancellationToken).ConfigureAwait(false);

        var sql = $"SELECT version, dirty FROM {GetQualifiedMigrationsTable()} LIMIT 1";
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        };

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new MigrationVersionState(null, false);
        }

        var versionValue = reader.GetInt64(0);
        var dirty = reader.GetBoolean(1);
        var version = versionValue == NilVersion ? (long?)null : versionValue;

        return new MigrationVersionState(version, dirty);
    }

    public async Task SetVersionAsync(long? version, bool isDirty, CancellationToken cancellationToken)
    {
        if (version.HasValue && version.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version cannot be negative.");
        }

        var connection = await GetConnectionAsync(cancellationToken, acquireLock: false).ConfigureAwait(false);
        await EnsureMigrationsTableAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var truncateSql = $"TRUNCATE {GetQualifiedMigrationsTable()}";
            await using (var truncate = new NpgsqlCommand(truncateSql, connection, transaction)
            {
                CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
            })
            {
                await truncate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var shouldInsert = version.HasValue || isDirty;
            if (shouldInsert)
            {
                var storedVersion = version ?? NilVersion;
                var insertSql = $"INSERT INTO {GetQualifiedMigrationsTable()} (version, dirty) VALUES ($1, $2)";
                await using var insert = new NpgsqlCommand(insertSql, connection, transaction)
                {
                    CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
                };

                insert.Parameters.AddWithValue(storedVersion);
                insert.Parameters.AddWithValue(isDirty);

                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DropAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken, acquireLock: false).ConfigureAwait(false);

        const string listSql = @"SELECT table_schema, table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema = current_schema()";
        await using var listCommand = new NpgsqlCommand(listSql, connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        };

        await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var tables = new List<(string Schema, string Table)>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        foreach (var (schema, table) in tables)
        {
            var dropSql = $"DROP TABLE IF EXISTS {QuoteIdentifier(schema)}.{QuoteIdentifier(table)} CASCADE";
            await using var dropCommand = new NpgsqlCommand(dropSql, connection)
            {
                CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
            };

            await dropCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ExecuteScriptAsync(MigrationScript script, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await GetConnectionAsync(cancellationToken, acquireLock: false).ConfigureAwait(false);

        if (_options.UseTransactions)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ExecuteScriptInternalAsync(connection, transaction, script, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        else
        {
            await ExecuteScriptInternalAsync(connection, null, script, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        return DisposeAsyncCore();
    }

    private async ValueTask DisposeAsyncCore()
    {
        await _connectionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_connection is not null)
            {
                await ReleaseAdvisoryLockAsync(_connection, CancellationToken.None).ConfigureAwait(false);
                await _connection.CloseAsync().ConfigureAwait(false);
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken, bool acquireLock)
    {
        if (_connection is not null)
        {
            if (acquireLock)
            {
                await EnsureAdvisoryLockAsync(_connection, cancellationToken).ConfigureAwait(false);
            }

            return _connection;
        }

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not null)
            {
                if (acquireLock)
                {
                    await EnsureAdvisoryLockAsync(_connection, cancellationToken).ConfigureAwait(false);
                }

                return _connection;
            }

            var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(_options.SearchPath))
            {
                await SetSearchPathAsync(connection, _options.SearchPath!, cancellationToken).ConfigureAwait(false);
            }

            if (acquireLock)
            {
                await EnsureAdvisoryLockAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            _connection = connection;
            return connection;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task EnsureAdvisoryLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (!_options.UseAdvisoryLocks || _lockHeld)
        {
            return;
        }

        _advisoryLockKey = _options.AdvisoryLockKey != 0
            ? _options.AdvisoryLockKey
            : GenerateAdvisoryLockId(
                _options.DatabaseName,
                GetMigrationsSchema(),
                GetMigrationsTable());

        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock($1);", connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        };
        command.Parameters.AddWithValue(_advisoryLockKey);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _lockHeld = true;
    }

    private async Task ReleaseAdvisoryLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (!_options.UseAdvisoryLocks || !_lockHeld)
        {
            _lockHeld = false;
            return;
        }

        await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock($1);", connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        };
        command.Parameters.AddWithValue(_advisoryLockKey);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _lockHeld = false;
    }

    private async Task EnsureMigrationsTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var schema = GetMigrationsSchema();
        var table = GetMigrationsTable();

        const string existsSql = @"SELECT COUNT(1) FROM information_schema.tables WHERE table_schema = $1 AND table_name = $2 LIMIT 1";
        await using var existsCommand = new NpgsqlCommand(existsSql, connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        };
        existsCommand.Parameters.AddWithValue(schema);
        existsCommand.Parameters.AddWithValue(table);

        var count = Convert.ToInt64(await existsCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (count > 0)
        {
            return;
        }

        var createSchemaSql = $"CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(schema)}";
        await using (var createSchema = new NpgsqlCommand(createSchemaSql, connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        })
        {
            await createSchema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var createTableSql = $"CREATE TABLE IF NOT EXISTS {GetQualifiedMigrationsTable()} (version bigint not null primary key, dirty boolean not null)";
        await using var createTable = new NpgsqlCommand(createTableSql, connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        };

        await createTable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteScriptInternalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        MigrationScript script,
        CancellationToken cancellationToken)
    {
        var content = await script.GetContentAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        if (_options.MultiStatementEnabled)
        {
            foreach (var statement in EnumerateStatements(content, _options.MultiStatementMaxSize))
            {
                if (string.IsNullOrWhiteSpace(statement))
                {
                    continue;
                }

                await ExecuteStatementAsync(connection, transaction, statement, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await ExecuteStatementAsync(connection, transaction, content, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteStatementAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string statement,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(statement, connection, transaction)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        };

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<string> EnumerateStatements(string content, int maxSize)
    {
        if (maxSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSize), maxSize, "Maximum statement size must be positive.");
        }

        var position = 0;
        while (position < content.Length)
        {
            var delimiterIndex = content.IndexOf(';', position);
            if (delimiterIndex < 0)
            {
                var tail = content[position..];
                if (tail.Length > 0)
                {
                    if (tail.Length > maxSize)
                    {
                        throw new MigrationException("Statement exceeds configured multi-statement maximum size.");
                    }

                    yield return tail;
                }
                yield break;
            }

            var length = delimiterIndex - position + 1;
            if (length > maxSize)
            {
                throw new MigrationException("Statement exceeds configured multi-statement maximum size.");
            }

            yield return content.Substring(position, length);
            position = delimiterIndex + 1;
        }
    }

    private async Task SetSearchPathAsync(NpgsqlConnection connection, string searchPath, CancellationToken cancellationToken)
    {
        var formatted = FormatSearchPath(searchPath);
        var sql = $"SET search_path TO {formatted}";
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        };

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string GetMigrationsSchema()
    {
        if (!string.IsNullOrWhiteSpace(_migrationsSchema))
        {
            return _migrationsSchema!;
        }

        if (!string.IsNullOrWhiteSpace(_options.MigrationsSchema))
        {
            _migrationsSchema = _options.MigrationsSchema;
            return _migrationsSchema!;
        }

        var searchPath = _options.SearchPath ?? _options.ConnectionSearchPath;
        if (!string.IsNullOrWhiteSpace(searchPath))
        {
            foreach (var segment in searchPath.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = segment.Trim();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    _migrationsSchema = candidate;
                    return _migrationsSchema!;
                }
            }
        }

        _migrationsSchema = "public";
        return _migrationsSchema!;
    }

    private string GetMigrationsTable()
    {
        if (!string.IsNullOrWhiteSpace(_migrationsTable))
        {
            return _migrationsTable!;
        }

        _migrationsTable = string.IsNullOrWhiteSpace(_options.MigrationsTable)
            ? "schema_migrations"
            : _options.MigrationsTable;

        return _migrationsTable!;
    }

    private string GetQualifiedMigrationsTable()
    {
        if (!string.IsNullOrWhiteSpace(_qualifiedMigrationsTable))
        {
            return _qualifiedMigrationsTable!;
        }

        _qualifiedMigrationsTable = $"{QuoteIdentifier(GetMigrationsSchema())}.{QuoteIdentifier(GetMigrationsTable())}";
        return _qualifiedMigrationsTable!;
    }

    private static string FormatSearchPath(string searchPath)
    {
        var builder = new StringBuilder();
        var first = true;

        foreach (var part in searchPath.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!first)
            {
                builder.Append(", ");
            }

            builder.Append(QuoteIdentifier(trimmed));
            first = false;
        }

        if (builder.Length == 0)
        {
            throw new ArgumentException("Search path must contain at least one schema name.", nameof(searchPath));
        }

        return builder.ToString();
    }

    private static string QuoteIdentifier(string identifier)
    {
        var escaped = identifier.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static long GenerateAdvisoryLockId(string databaseName, string schemaName, string tableName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name must be provided for advisory lock generation.", nameof(databaseName));
        }

        if (string.IsNullOrWhiteSpace(schemaName))
        {
            throw new ArgumentException("Schema name must be provided for advisory lock generation.", nameof(schemaName));
        }

        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name must be provided for advisory lock generation.", nameof(tableName));
        }

        var joined = string.Join('\x00', schemaName, tableName, databaseName);
        var bytes = Encoding.UTF8.GetBytes(joined);
        Span<byte> hash = stackalloc byte[4];
        Crc32.Hash(bytes, hash);
        var sum = BitConverter.ToUInt32(hash);
        sum *= 1486364155u;
        return unchecked((long)sum);
    }
}
