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

internal sealed class GlobalOptionsReader
{
    private readonly Option<string?> _sourceOption;
    private readonly Option<string?> _pathOption;
    private readonly Option<string?> _databaseOption;
    private readonly Option<uint> _prefetchOption;
    private readonly Option<uint> _lockTimeoutOption;
    private readonly Option<bool> _verboseOption;

    public GlobalOptionsReader(
        Option<string?> sourceOption,
        Option<string?> pathOption,
        Option<string?> databaseOption,
        Option<uint> prefetchOption,
        Option<uint> lockTimeoutOption,
        Option<bool> verboseOption)
    {
        _sourceOption = sourceOption;
        _pathOption = pathOption;
        _databaseOption = databaseOption;
        _prefetchOption = prefetchOption;
        _lockTimeoutOption = lockTimeoutOption;
        _verboseOption = verboseOption;
    }

    public GlobalOptions Read(ParseResult parseResult)
        => new(
            parseResult.GetValueForOption(_sourceOption),
            parseResult.GetValueForOption(_pathOption),
            parseResult.GetValueForOption(_databaseOption),
            parseResult.GetValueForOption(_prefetchOption),
            parseResult.GetValueForOption(_lockTimeoutOption),
            parseResult.GetValueForOption(_verboseOption));
}
