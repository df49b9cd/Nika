using System.Globalization;
using System.Text;

namespace Nika.Cli;

internal sealed class MigrationFileScaffolder
{
    public const string DefaultTimestampFormat = "20060102150405";

    private readonly string _directory;

    public MigrationFileScaffolder(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Directory must be provided.", nameof(directory));
        }

        _directory = Path.GetFullPath(directory);
    }

    public async Task<MigrationScaffoldResult> ScaffoldAsync(
        string name,
        string extension,
        bool sequential,
        int digits,
        string format,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_directory);

        var ext = "." + extension.TrimStart('.');
        if (sequential && digits <= 0)
        {
            throw new CliUsageException("Digits must be positive when using --seq.");
        }

        var effectiveFormat = string.IsNullOrWhiteSpace(format)
            ? DefaultTimestampFormat
            : format;

        var version = sequential
            ? GetNextSequentialVersion(ext, digits)
            : GetTimestampVersion(effectiveFormat, timeZone);

        EnsureVersionIsUnique(version, ext);

        var nameSegment = SanitizeName(name);
        var upPath = Path.Combine(_directory, $"{version}_{nameSegment}.up{ext}");
        var downPath = Path.Combine(_directory, $"{version}_{nameSegment}.down{ext}");

        CreateEmptyFile(upPath);
        CreateEmptyFile(downPath);

        await Task.CompletedTask.ConfigureAwait(false);

        return new MigrationScaffoldResult([upPath, downPath]);
    }

    private string GetNextSequentialVersion(string extension, int digits)
    {
        var files = Directory.GetFiles(_directory, $"*{extension}", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ulong nextValue = 1;

        if (files.Count > 0)
        {
            var last = files[^1];
            var underscoreIndex = last.IndexOf('_');
            if (underscoreIndex <= 0)
            {
                throw new CliUsageException($"Malformed migration filename: {last}");
            }

            var versionPart = last[..underscoreIndex];
            if (!ulong.TryParse(versionPart, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new CliUsageException($"Malformed migration filename: {last}");
            }

            nextValue = parsed + 1;
        }

        var formatted = nextValue.ToString(new string('0', digits), CultureInfo.InvariantCulture);
        if (formatted.Length > digits)
        {
            throw new CliUsageException($"Next sequence number {formatted} exceeds the configured digit width.");
        }

        return formatted;
    }

    private string GetTimestampVersion(string format, TimeZoneInfo timeZone)
    {
        if (string.Equals(format, "unix", StringComparison.OrdinalIgnoreCase))
        {
            var unixSeconds = (long)(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).ToUnixTimeSeconds());
            return unixSeconds.ToString(CultureInfo.InvariantCulture);
        }

        if (string.Equals(format, "unixNano", StringComparison.OrdinalIgnoreCase))
        {
            var unixNanoseconds = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).ToUnixTimeMilliseconds() * 1_000_000;
            return unixNanoseconds.ToString(CultureInfo.InvariantCulture);
        }

        var dotNetFormat = ConvertGoLayoutToDotNet(format);
        var now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, timeZone);
        return now.ToString(dotNetFormat, CultureInfo.InvariantCulture);
    }

    private void EnsureVersionIsUnique(string version, string extension)
    {
        var existing = Directory.GetFiles(_directory, $"{version}_*{extension}", SearchOption.TopDirectoryOnly);
        if (existing.Length > 0)
        {
            throw new CliUsageException($"Duplicate migration version: {version}");
        }
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "migration";
        }

        var builder = new StringBuilder(name.Length);
        var invalid = Path.GetInvalidFileNameChars();

        foreach (var c in name.Trim())
        {
            if (invalid.Contains(c))
            {
                builder.Append('_');
            }
            else if (char.IsWhiteSpace(c))
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string ConvertGoLayoutToDotNet(string layout)
    {
        var builder = new StringBuilder(layout.Length * 2);

        for (var i = 0; i < layout.Length;)
        {
            if (Match(layout, i, "2006"))
            {
                builder.Append("yyyy");
                i += 4;
            }
            else if (Match(layout, i, "06"))
            {
                builder.Append("yy");
                i += 2;
            }
            else if (Match(layout, i, "01"))
            {
                builder.Append("MM");
                i += 2;
            }
            else if (Match(layout, i, "02"))
            {
                builder.Append("dd");
                i += 2;
            }
            else if (Match(layout, i, "15"))
            {
                builder.Append("HH");
                i += 2;
            }
            else if (Match(layout, i, "03"))
            {
                builder.Append("hh");
                i += 2;
            }
            else if (Match(layout, i, "04"))
            {
                builder.Append("mm");
                i += 2;
            }
            else if (Match(layout, i, "05"))
            {
                builder.Append("ss");
                i += 2;
            }
            else if (Match(layout, i, "PM"))
            {
                builder.Append("tt");
                i += 2;
            }
            else if (layout[i] == '.')
            {
                var count = CountRepeated(layout, i + 1, '0');
                if (count == 0)
                {
                    builder.Append('.');
                    i++;
                }
                else
                {
                    builder.Append('.');
                    builder.Append(new string('f', count));
                    i += count + 1;
                }
            }
            else
            {
                builder.Append(layout[i]);
                i++;
            }
        }

        return builder.ToString();
    }

    private static bool Match(string value, int index, string token)
        => index + token.Length <= value.Length && string.Compare(value, index, token, 0, token.Length, StringComparison.Ordinal) == 0;

    private static int CountRepeated(string value, int startIndex, char target)
    {
        var count = 0;
        while (startIndex + count < value.Length && value[startIndex + count] == target)
        {
            count++;
        }

        return count;
    }

    private static void CreateEmptyFile(string path)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Close();
    }
}

internal sealed record MigrationScaffoldResult(IReadOnlyList<string> CreatedFiles);
