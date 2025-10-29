using System.CommandLine;
using System.CommandLine.IO;

namespace Nika.Cli;

internal sealed class StandardConsole : IConsole
{
    public IStandardStreamWriter Out { get; } = new StreamWriterAdapter(Console.Out);

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public IStandardStreamWriter Error { get; } = new StreamWriterAdapter(Console.Error);

    public bool IsErrorRedirected => Console.IsErrorRedirected;

    public bool IsInputRedirected => Console.IsInputRedirected;

    private sealed class StreamWriterAdapter(TextWriter writer) : IStandardStreamWriter
    {
        private readonly TextWriter _writer = writer;

        public void Write(string? value)
        {
            if (value is not null)
            {
                _writer.Write(value);
            }
            _writer.Flush();
        }
    }
}
