# Technical Baseline

Nika targets the latest Long-Term Support release of .NET to balance platform reach with ecosystem stability.

- **Runtime**: .NET 9 (net9.0)
- **Language Version**: C# 12
- **Build Tooling**: `dotnet` CLI (SDK 9.0.100 or newer)
- **Test Framework**: xUnit with `dotnet test`
- **Static Analysis**: Roslyn analyzers enabled with warnings-as-errors for the core library
- **OS Support**: Windows, Linux, and macOS (x64/arm64) via cross-platform runtime

### Development Environment
- Install the .NET 9 SDK from [https://dotnet.microsoft.com/download](https://dotnet.microsoft.com/download) or via your package manager.
- Optional: Install .NET 10 preview SDKs in parallel for forward-looking validation, but ensure CI runs against the LTS baseline.
- Use the provided solution (`Nika.sln`) to open the workspace in JetBrains Rider, Visual Studio, or VS Code.

### Repository Structure
- `src/` – production code (class libraries, CLI host, adapters)
- `tests/` – unit, integration, and scenario test projects
- `docs/` – design, mission, roadmap, and contributor documentation
- `.github/` – CI and community health files

This baseline will evolve as new .NET LTS versions ship. Update this document and the solution configuration whenever the minimum runtime changes.
