using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nika.Migrations.Sources;

/// <summary>
/// Discovers migrations from a filesystem directory using a naming convention of
/// &lt;version&gt;_description.up.sql / &lt;version&gt;_description.down.sql.
/// </summary>
public sealed class FileSystemMigrationSource : IMigrationSource
{
    private static readonly Regex FilePattern = new(
        @"^(?<version>\d+)_?(?<name>.*?)\.(?<direction>up|down)\.(?<extension>sql|txt)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _directory;

    public FileSystemMigrationSource(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Directory path must be provided.", nameof(directory));
        }

        _directory = Path.GetFullPath(directory);
    }

    public Task<IReadOnlyCollection<Migration>> LoadMigrationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_directory))
        {
            throw new DirectoryNotFoundException($"Migration directory '{_directory}' was not found.");
        }

        var descriptors = new Dictionary<long, FileMigrationDefinition>();

        foreach (var file in Directory.EnumerateFiles(_directory, "*.*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            var match = FilePattern.Match(fileName);
            if (!match.Success)
            {
                continue;
            }

            var version = long.Parse(match.Groups["version"].Value);
            var direction = match.Groups["direction"].Value.ToLowerInvariant() == "up"
                ? MigrationDirection.Up
                : MigrationDirection.Down;
            var description = match.Groups["name"].Value.Replace('_', ' ').Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                description = $"Migration {version}";
            }

            if (!descriptors.TryGetValue(version, out var descriptor))
            {
                descriptor = new FileMigrationDefinition(version, description);
                descriptors[version] = descriptor;
            }

            switch (direction)
            {
                case MigrationDirection.Up:
                    if (!string.IsNullOrEmpty(descriptor.UpPath))
                    {
                        throw new MigrationException($"Duplicate up migration detected for version {version}.");
                    }

                    descriptor.UpPath = file;
                    break;
                case MigrationDirection.Down:
                    if (!string.IsNullOrEmpty(descriptor.DownPath))
                    {
                        throw new MigrationException($"Duplicate down migration detected for version {version}.");
                    }

                    descriptor.DownPath = file;
                    break;
            }
        }

        var versions = new List<long>(descriptors.Keys);
        versions.Sort();

        var migrations = new List<Migration>(versions.Count);
        foreach (var version in versions)
        {
            migrations.Add(descriptors[version].ToMigration());
        }

        return Task.FromResult<IReadOnlyCollection<Migration>>(migrations);
    }

    private sealed class FileMigrationDefinition
    {
        public FileMigrationDefinition(long version, string description)
        {
            Version = version;
            Description = description;
        }

        public long Version { get; }

        public string Description { get; }

        public string? UpPath { get; set; }

        public string? DownPath { get; set; }

        public Migration ToMigration()
        {
            if (string.IsNullOrEmpty(UpPath))
            {
                throw new MigrationException($"Missing up migration file for version {Version}.");
            }

            return Migration.Create(
                Version,
                Description,
                async (driver, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var scriptDriver = driver as IScriptMigrationDriver
                        ?? throw new MigrationException("Driver does not support script execution.");

                    var script = new MigrationScript(
                        Version,
                        Description,
                        MigrationDirection.Up,
                        UpPath!,
                        ct => File.ReadAllTextAsync(UpPath!, ct));

                    await scriptDriver.ExecuteScriptAsync(script, cancellationToken).ConfigureAwait(false);
                },
                async (driver, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(DownPath))
                    {
                        throw new MigrationException($"Missing down migration file for version {Version}.");
                    }

                    var scriptDriver = driver as IScriptMigrationDriver
                        ?? throw new MigrationException("Driver does not support script execution.");

                    var script = new MigrationScript(
                        Version,
                        Description,
                        MigrationDirection.Down,
                        DownPath!,
                        ct => File.ReadAllTextAsync(DownPath!, ct));

                    await scriptDriver.ExecuteScriptAsync(script, cancellationToken).ConfigureAwait(false);
                });
        }
    }
}
