# Contributing to Nika

Thanks for your interest in helping build Nika! This guide outlines the workflow and expectations for contributors during the early development phases.

## Prerequisites

- Install the [.NET 9 SDK](https://dotnet.microsoft.com/download) (9.0.1 or newer).
- Install Docker if you plan to work on integration tests.
- Clone the repository and ensure submodules (if any) are initialized.

## Repository Layout

- `src/` – production projects (`Nika`, `Nika.Cli`, and future adapters)
- `tests/` – unit and integration test projects
- `docs/` – design documents, mission statement, roadmap, and guides
- `.github/workflows` – CI configuration

See [`docs/technical-baseline.md`](docs/technical-baseline.md) for the supported runtime and language versions.

## Development Workflow

1. Create a feature branch from `main`.
2. Restore dependencies and ensure formatting is clean:

   ```bash
   dotnet restore Nika.sln
   dotnet format Nika.sln
   ```

3. Build and test before opening a pull request:

   ```bash
   dotnet build Nika.sln --configuration Release
   dotnet test Nika.sln --configuration Release
   ```

4. Update or add tests for any new functionality.
5. Keep documentation in sync—mission, philosophy, or driver guides should evolve alongside the code.

## Coding Standards

- Nullable reference types must remain enabled.
- Treat warnings as errors in library and CLI projects (configured via `Directory.Build.props`).
- Follow the style enforced by `.editorconfig`. Run `dotnet format` before committing.
- Favor explicit failures over silent coercion, mirroring `golang-migrate`’s philosophy.

## Commit & PR Guidelines

- Write descriptive commit messages that explain the “why”.
- Reference related issues in the pull request description.
- Include screenshots or logs when modifying tooling or CI.
- Make sure CI passes before requesting review.

## Getting Help

- Open a GitHub Discussion or issue if you run into blockers.
- Mention documentation updates in your PR when you add new features or change behavior.

We appreciate your contributions and look forward to building a reliable migration toolkit together.
