using System.CommandLine;
using System.CommandLine.Parsing;

namespace Nika.Cli;

internal sealed record GlobalOptions(
    string? Source,
    string? Path,
    string? Database,
    uint Prefetch,
    uint LockTimeout,
    bool Verbose);

internal sealed class GlobalOptionsReader(
    Option<string?> sourceOption,
    Option<string?> pathOption,
    Option<string?> databaseOption,
    Option<uint> prefetchOption,
    Option<uint> lockTimeoutOption,
    Option<bool> verboseOption)
{
    private readonly Option<string?> _sourceOption = sourceOption;
    private readonly Option<string?> _pathOption = pathOption;
    private readonly Option<string?> _databaseOption = databaseOption;
    private readonly Option<uint> _prefetchOption = prefetchOption;
    private readonly Option<uint> _lockTimeoutOption = lockTimeoutOption;
    private readonly Option<bool> _verboseOption = verboseOption;

    public GlobalOptions Read(ParseResult parseResult)
        => new(
            parseResult.GetValueForOption(_sourceOption),
            parseResult.GetValueForOption(_pathOption),
            parseResult.GetValueForOption(_databaseOption),
            parseResult.GetValueForOption(_prefetchOption),
            parseResult.GetValueForOption(_lockTimeoutOption),
            parseResult.GetValueForOption(_verboseOption));
}
