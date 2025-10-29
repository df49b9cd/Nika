using Xunit;

namespace Nika.Tests;

public class RuntimeFacts
{
    [Fact]
    public void MinimumRuntimeMatchesDocumentation()
    {
        Assert.Equal("net9.0", NikaRuntime.MinimumRuntime);
    }
}
