using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Nika.Migrations;

namespace Nika.Migrations.Drivers.SqlServer;

public sealed class SqlServerScriptMigrationDriver(SqlServerScriptMigrationDriverOptions options) : IScriptMigrationDriver
{
    private const long NilVersion = -1;
    private static readonly Regex BatchSeparator = new("^\\s*GO(?:\\s+(?<count>\\d+))?\\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly IReadOnlyDictionary<int, string> LockErrorMessages = new Dictionary<int, string>
    {
        { -1, "The lock request timed out." },
        { -2, "The lock request was canceled." },
        { -3, "The lock request was chosen as a deadlock victim." },
        { -999, "Parameter validation or other call error." },
    };

    private readonly SqlServerScriptMigrationDriverOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    private SqlConnection? _connection;
    private bool _lockHeld;
    private bool _disposed;
    private string? _schemaName;
    private string? _tableName;
    private string? _qualifiedTable;

    public async Task LockAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken, acquireLock: true).ConfigureAwait(false);
        await EnsureVersionTableAsync(connection, cancellationToken).ConfigureAwait(false);
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

            await ReleaseApplicationLockAsync(_connection, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<MigrationVersionState> GetVersionStateAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken, acquireLock: false).ConfigureAwait(false);
        await EnsureVersionTableAsync(connection, cancellationToken).ConfigureAwait(false);

        var sql = $"SELECT TOP (1) [version], [dirty] FROM {GetQualifiedTable()}";
        await using var command = new SqlCommand(sql, connection)
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
        await EnsureVersionTableAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var truncateSql = $"TRUNCATE TABLE {GetQualifiedTable()}";
            await using (var truncateCommand = new SqlCommand(truncateSql, connection, transaction)
            {
                CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
            })
            {
                await truncateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var shouldInsert = version.HasValue && version.Value >= 0 || (!version.HasValue && isDirty);
            if (shouldInsert)
            {
                var storedVersion = version ?? NilVersion;
                var dirtyBit = isDirty ? 1 : 0;
                var insertSql = $"INSERT INTO {GetQualifiedTable()} ([version], [dirty]) VALUES (@version, @dirty)";
                await using var insertCommand = new SqlCommand(insertSql, connection, transaction)
                {
                    CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
                };

                insertCommand.Parameters.AddWithValue("@version", storedVersion);
                insertCommand.Parameters.AddWithValue("@dirty", dirtyBit);

                await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

        const string dropConstraints = @"
DECLARE @Sql NVARCHAR(500) DECLARE @Cursor CURSOR

SET @Cursor = CURSOR FAST_FORWARD FOR
SELECT DISTINCT sql = 'ALTER TABLE [' + tc2.TABLE_SCHEMA + '].[' + tc2.TABLE_NAME + '] DROP CONSTRAINT [' + rc1.CONSTRAINT_NAME + ']'
FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc1
LEFT JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc2 ON tc2.CONSTRAINT_NAME = rc1.CONSTRAINT_NAME

OPEN @Cursor FETCH NEXT FROM @Cursor INTO @Sql

WHILE (@@FETCH_STATUS = 0)
BEGIN
    EXEC sp_executesql @Sql
    FETCH NEXT FROM @Cursor INTO @Sql
END

CLOSE @Cursor DEALLOCATE @Cursor";

        await using (var dropConstraintCommand = new SqlCommand(dropConstraints, connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        })
        {
            await dropConstraintCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        const string dropTables = "EXEC sp_MSforeachtable 'DROP TABLE ?'";
        await using (var dropTablesCommand = new SqlCommand(dropTables, connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        })
        {
            await dropTablesCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ExecuteScriptAsync(MigrationScript script, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var content = await script.GetContentAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var connection = await GetConnectionAsync(cancellationToken, acquireLock: false).ConfigureAwait(false);

        if (_options.UseTransactions)
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ExecuteBatchesAsync(connection, transaction, content, cancellationToken).ConfigureAwait(false);
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
            await ExecuteBatchesAsync(connection, null, content, cancellationToken).ConfigureAwait(false);
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
                await ReleaseApplicationLockAsync(_connection, CancellationToken.None).ConfigureAwait(false);
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

    private async Task<SqlConnection> GetConnectionAsync(CancellationToken cancellationToken, bool acquireLock)
    {
        if (_connection is not null)
        {
            if (acquireLock)
            {
                await EnsureApplicationLockAsync(_connection, cancellationToken).ConfigureAwait(false);
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
                    await EnsureApplicationLockAsync(_connection, cancellationToken).ConfigureAwait(false);
                }

                return _connection;
            }

            var connection = new SqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            if (acquireLock)
            {
                await EnsureApplicationLockAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            _connection = connection;
            return connection;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task EnsureApplicationLockAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (_lockHeld)
        {
            return;
        }

        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        var resource = $"nika:{GetQualifiedTable()}";
        const string lockSql = @"
DECLARE @result INT;
EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @timeout;
SELECT @result;";

        await using var command = new SqlCommand(lockSql, connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        };
        command.Parameters.AddWithValue("@resource", resource);
        command.Parameters.AddWithValue("@timeout", Convert.ToInt32(_options.CommandTimeout.TotalMilliseconds));

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var result = Convert.ToInt32(scalar ?? 0, CultureInfo.InvariantCulture);
        if (result < 0)
        {
            throw new MigrationException(LockErrorMessages.TryGetValue(result, out var message)
                ? message
                : $"Failed to acquire SQL Server application lock (code {result}).");
        }

        _lockHeld = true;
    }

    private async Task ReleaseApplicationLockAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (!_lockHeld)
        {
            return;
        }

        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        var resource = $"nika:{GetQualifiedTable()}";
        const string unlockSql = @"
DECLARE @result INT;
EXEC @result = sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';
SELECT @result;";

        await using var command = new SqlCommand(unlockSql, connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        };
        command.Parameters.AddWithValue("@resource", resource);

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var result = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
        if (result < 0)
        {
            throw new MigrationException($"Failed to release SQL Server application lock (code {result}).");
        }

        _lockHeld = false;
    }

    private async Task EnsureVersionTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        var schema = _schemaName!;
        var table = _tableName!;

        var sql = @"
IF NOT EXISTS (
    SELECT *
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table)
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = @schema)
    BEGIN
        DECLARE @createSchema NVARCHAR(400);
        SET @createSchema = 'CREATE SCHEMA ' + QUOTENAME(@schema);
        EXEC (@createSchema);
    END

    DECLARE @createTable NVARCHAR(MAX);
    SET @createTable = 'CREATE TABLE ' + QUOTENAME(@schema) + '.' + QUOTENAME(@table) + ' ([version] BIGINT NOT NULL PRIMARY KEY, [dirty] BIT NOT NULL)';
    EXEC (@createTable);
END";

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
        };

        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteBatchesAsync(SqlConnection connection, SqlTransaction? transaction, string script, CancellationToken cancellationToken)
    {
        await foreach (var batch in EnumerateBatchesAsync(script).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(batch.CommandText))
            {
                continue;
            }

            for (var iteration = 0; iteration < batch.RepeatCount; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = new SqlCommand(batch.CommandText, connection, transaction)
                {
                    CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
                };

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async IAsyncEnumerable<SqlBatch> EnumerateBatchesAsync(string script)
    {
        await Task.Yield();

        using var reader = new StringReader(script);
        var current = new List<string>();

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var match = BatchSeparator.Match(line);
            if (match.Success)
            {
                var countGroup = match.Groups["count"];
                var repeatCount = 1;
                if (countGroup.Success && int.TryParse(countGroup.Value, out var parsed) && parsed > 0)
                {
                    repeatCount = parsed;
                }

                var batch = CreateBatch(current, repeatCount);
                if (!string.IsNullOrWhiteSpace(batch.CommandText))
                {
                    yield return batch;
                }

                current.Clear();
                continue;
            }

            current.Add(line);
        }

        var finalBatch = CreateBatch(current, 1);
        if (!string.IsNullOrWhiteSpace(finalBatch.CommandText))
        {
            yield return finalBatch;
        }
    }

    private static SqlBatch CreateBatch(List<string> lines, int repeatCount)
    {
        if (lines.Count == 0)
        {
            return new SqlBatch(string.Empty, repeatCount);
        }

        var text = string.Join(Environment.NewLine, lines).Trim();
        return new SqlBatch(text, repeatCount);
    }

    private string GetQualifiedTable()
    {
        if (!string.IsNullOrWhiteSpace(_qualifiedTable))
        {
            return _qualifiedTable!;
        }

        if (string.IsNullOrWhiteSpace(_schemaName) || string.IsNullOrWhiteSpace(_tableName))
        {
            throw new MigrationException("Migration table name has not been resolved yet.");
        }

        _qualifiedTable = $"{BracketIdentifier(_schemaName!)}.{BracketIdentifier(_tableName!)}";
        return _qualifiedTable!;
    }

    private static string BracketIdentifier(string identifier)
        => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private async Task EnsureSchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_schemaName) && !string.IsNullOrWhiteSpace(_tableName))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_schemaName))
        {
            if (!string.IsNullOrWhiteSpace(_options.MigrationsSchema))
            {
                _schemaName = _options.MigrationsSchema;
            }
            else
            {
                const string schemaSql = "SELECT SCHEMA_NAME()";
                await using var schemaCommand = new SqlCommand(schemaSql, connection)
                {
                    CommandTimeout = (int)_options.CommandTimeout.TotalSeconds,
                };

                var result = await schemaCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                _schemaName = Convert.ToString(result, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(_schemaName))
                {
                    throw new MigrationException("Unable to resolve default schema name.");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(_tableName))
        {
            _tableName = string.IsNullOrWhiteSpace(_options.MigrationsTable)
                ? "schema_migrations"
                : _options.MigrationsTable;
        }

        _qualifiedTable = null;
    }

    private readonly record struct SqlBatch(string CommandText, int RepeatCount);
}
