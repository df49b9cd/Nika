namespace Nika.Migrations;

/// <summary>
/// Represents a single versioned migration with executable Up/Down delegates.
/// </summary>
public sealed class Migration
{
    private readonly Func<IMigrationDriver, CancellationToken, Task> _applyAsync;
    private readonly Func<IMigrationDriver, CancellationToken, Task> _revertAsync;

    private Migration(
        long version,
        string description,
        Func<IMigrationDriver, CancellationToken, Task> applyAsync,
        Func<IMigrationDriver, CancellationToken, Task> revertAsync)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Migration version must be positive.");
        }

        Version = version;
        Description = description ?? string.Empty;
        _applyAsync = applyAsync ?? throw new ArgumentNullException(nameof(applyAsync));
        _revertAsync = revertAsync ?? throw new ArgumentNullException(nameof(revertAsync));
    }

    public long Version { get; }

    public string Description { get; }

    /// <summary>
    /// Creates a migration using asynchronous delegates.
    /// </summary>
    public static Migration Create(
        long version,
        string description,
        Func<IMigrationDriver, CancellationToken, Task> applyAsync,
        Func<IMigrationDriver, CancellationToken, Task> revertAsync)
        => new(version, description, applyAsync, revertAsync);

    /// <summary>
    /// Creates a migration using synchronous delegates.
    /// </summary>
    public static Migration Create(
        long version,
        string description,
        Action<IMigrationDriver> apply,
        Action<IMigrationDriver> revert)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(revert);

        return new Migration(
            version,
            description,
            (driver, _) =>
            {
                apply(driver);
                return Task.CompletedTask;
            },
            (driver, _) =>
            {
                revert(driver);
                return Task.CompletedTask;
            });
    }

    internal Task ApplyAsync(IMigrationDriver driver, CancellationToken cancellationToken)
        => _applyAsync(driver, cancellationToken);

    internal Task RevertAsync(IMigrationDriver driver, CancellationToken cancellationToken)
        => _revertAsync(driver, cancellationToken);
}
