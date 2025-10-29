using System.CommandLine;
using System.CommandLine.Parsing;
using Nika.Cli;

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

var parser = CommandApp.Build(VersionProvider.GetVersion(), cts.Token);
var console = new StandardConsole();
var parseResult = parser.Parse(args);
return await parseResult.InvokeAsync(console);
