using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nika.Migrations;

public sealed class MigrationScript
{
    private readonly Func<CancellationToken, Task<string>> _contentFactory;

    public MigrationScript(
        long version,
        string description,
        MigrationDirection direction,
        string path,
        Func<CancellationToken, Task<string>> contentFactory)
    {
        Version = version;
        Description = description ?? string.Empty;
        Direction = direction;
        Path = path ?? throw new ArgumentNullException(nameof(path));
        _contentFactory = contentFactory ?? throw new ArgumentNullException(nameof(contentFactory));
    }

    public MigrationScript(long version, string description, MigrationDirection direction, string path, string content)
        : this(version, description, direction, path, _ => Task.FromResult(content ?? string.Empty))
    {
    }

    public long Version { get; }

    public string Description { get; }

    public MigrationDirection Direction { get; }

    public string Path { get; }

    public Task<string> GetContentAsync(CancellationToken cancellationToken)
        => _contentFactory(cancellationToken);
}

public enum MigrationDirection
{
    Up,
    Down,
}
