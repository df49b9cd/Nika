using System;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;

namespace Nika.Cli;

internal sealed class StandardConsole : IConsole
{
    public StandardConsole()
    {
        Out = new StreamWriterAdapter(Console.Out);
        Error = new StreamWriterAdapter(Console.Error);
    }

    public IStandardStreamWriter Out { get; }

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public IStandardStreamWriter Error { get; }

    public bool IsErrorRedirected => Console.IsErrorRedirected;

    public bool IsInputRedirected => Console.IsInputRedirected;

    private sealed class StreamWriterAdapter : IStandardStreamWriter
    {
        private readonly TextWriter _writer;

        public StreamWriterAdapter(TextWriter writer)
        {
            _writer = writer;
        }

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
