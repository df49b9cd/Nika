using System.Reflection;

namespace Nika.Cli;

internal static class VersionProvider
{
    public static string GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational!;
        }

        var version = assembly.GetName().Version;
        return version?.ToString() ?? "unknown";
    }
}
