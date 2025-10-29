using System;

namespace Nika.Tests;

public sealed class SkipException : Exception
{
    public SkipException(string message)
        : base(message)
    {
    }
}
