using System.Text.Json;

namespace Nika.Migrations;

/// <summary>
/// An <see cref="IVersionStore"/> implementation that persists migration version state to a JSON file.
/// </summary>
public sealed class FileVersionStore : IVersionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileVersionStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must be provided.", nameof(filePath));
        }

        _filePath = Path.GetFullPath(filePath);
    }

    public async Task<MigrationVersionState> GetVersionStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return new MigrationVersionState(null, false);
            }

            await using var stream = File.OpenRead(_filePath);
            var payload = await JsonSerializer.DeserializeAsync<StatePayload>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false) ?? new StatePayload();

            return new MigrationVersionState(payload.Version, payload.IsDirty);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetVersionStateAsync(long? version, bool isDirty, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (version.HasValue && version.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version cannot be negative.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = new StatePayload
            {
                Version = version,
                IsDirty = isDirty,
            };

            var tempPath = _filePath + ".tmp";
            await using (var stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    payload,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }

            File.Move(tempPath, _filePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class StatePayload
    {
        public long? Version { get; set; }

        public bool IsDirty { get; set; }
    }
}
