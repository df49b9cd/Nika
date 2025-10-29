using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nika.Migrations;

public sealed class MigrationScript(
    long version,
    string description,
    MigrationDirection direction,
    string path,
    Func<CancellationToken, Task<string>> contentFactory)
{
    private readonly Func<CancellationToken, Task<string>> _contentFactory = contentFactory ?? throw new ArgumentNullException(nameof(contentFactory));

    public MigrationScript(long version, string description, MigrationDirection direction, string path, string content)
        : this(version, description, direction, path, _ => Task.FromResult(content ?? string.Empty))
    {
    }

    public long Version { get; } = version;

    public string Description { get; } = description ?? string.Empty;

    public MigrationDirection Direction { get; } = direction;

    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    public Task<string> GetContentAsync(CancellationToken cancellationToken)
        => _contentFactory(cancellationToken);
}

public enum MigrationDirection
{
    Up,
    Down,
}
