# Nika Roadmap

This roadmap translates Nika’s mission and implementation philosophy into actionable phases. Each phase builds on the previous one, keeping feature parity with `golang-migrate` front of mind while adopting idiomatic .NET practices.

## Phase 0 – Foundation & Planning

- **Deliverables**
  - Confirm minimum supported .NET runtime and language version.
  - Finalize repository layout (`src`, `tests`, `docs`) and solution/project scaffolding.
  - Define coding standards, formatter/analyzer configuration, and CI baseline (lint + unit test).
  - Publish contributor workflow in `CONTRIBUTING.md` and update documentation index.
- **Dependencies**: none.
- **Success Criteria**: repo bootstrapped; developers can clone, build, and run placeholder tests.

## Phase 1 – Core Library Prototype

- **Deliverables**
  - Implement core migration engine with APIs equivalent to `migrate.New`, `Up`, `Down`, `Steps`, `Force`, `Version`.
  - Introduce migration registry and version-tracking persistence abstraction.
  - Support graceful stop/cancellation tokens mirroring `GracefulStop`.
  - Provide in-memory source + stub driver for integration tests.
  - Write unit tests for runner logic, ordering, dirty-state handling, and edge cases (empty migrations, missing pairs).
- **Dependencies**: Phase 0 complete.
- **Success Criteria**: .NET consumers can execute migrations using in-memory source and stub driver; tests cover control flow.

## Phase 2 – Filesystem Source & PostgreSQL Driver (Reference Implementations)

- **Deliverables**
  - Implement `FileSystemSource` with versioned file discovery and streaming access.
  - Implement PostgreSQL driver honoring transactions, advisory locks, and dirty-state flags.
  - Create connection string parser with strong validation (fail fast on ambiguity).
  - Provide configuration options for transactional vs non-transactional execution.
  - Add docker-backed integration test harness for PostgreSQL versions supported upstream.
  - Document usage in “Getting Started” for library consumers.
- **Dependencies**: Phase 1 complete; Docker setup from Phase 0.
- **Success Criteria**: Real migrations run against PostgreSQL via automated tests; docs explain setup.

## Phase 3 – CLI Implementation

- **Deliverables**
  - Ship cross-platform CLI replicating core golang-migrate commands (`up`, `down`, `force`, `goto`, `version`, `drop`, `create`).
  - Implement consistent flag/ENV handling (no hidden config search paths).
  - Add signal handling for `CTRL+C` to trigger graceful stop.
  - Package self-contained binaries using .NET single-file publish configuration.
  - Expand docs with CLI quickstart and troubleshooting section.
- **Dependencies**: Phases 1–2 complete.
- **Success Criteria**: CLI passes parity acceptance tests; works on macOS, Linux, Windows runners.

## Phase 4 – Additional First-Party Drivers

- **Deliverables**
  - Prioritize MySQL/MariaDB and SQL Server drivers, followed by SQLite and MongoDB.
  - Ensure each driver supports native locking (or document limitations) and transactional semantics where possible.
  - Extend integration harness to spin up database containers per driver.
  - Provide driver-specific README sections and sample connection strings.
- **Dependencies**: Phase 2 infrastructure, Phase 3 CLI for manual verification.
- **Success Criteria**: Drivers ship with automated coverage and documented caveats; CLI/library work seamlessly across supported databases.

## Phase 5 – Additional Migration Sources

- **Deliverables**
  - Implement embedded resource source (e.g. .NET `ResourceManager` / `System.Reflection`).
  - Add remote sources: GitHub, S3-compatible storage, Zip archives.
  - Include caching and retry policies documented per source.
  - Add integration tests using local mocks (e.g. MinIO for S3).
  - Document source URL formats, authentication, and failure modes.
- **Dependencies**: Phase 1 core abstractions stable; any remote source requires credential management in CI.
- **Success Criteria**: Sources demonstrate parity with golang-migrate behavior and maintain streaming guarantees.

## Phase 6 – Observability & Tooling

- **Deliverables**
  - Introduce logging abstractions with structured output; provide adapters for common .NET logging frameworks.
  - Add metrics hooks (e.g. counters for applied migrations, durations) behind optional interfaces.
  - Implement dry-run/report mode for previewing pending migrations.
  - Surface detailed error codes and troubleshooting guidance.
- **Dependencies**: Phases 1–3 complete.
- **Success Criteria**: Operators can integrate Nika with monitoring stacks; dry-run aids deployment reviews.

## Phase 7 – Packaging & Distribution

- **Deliverables**
  - Publish NuGet packages for core library and drivers.
  - Release CLI binaries via GitHub Releases with checksums.
  - Provide versioning policy aligned with semantic versioning and golang-migrate compatibility promises.
  - Automate release notes generation and changelog updates.
- **Dependencies**: Core features stabilized; testing pipeline reliable.
- **Success Criteria**: Consumers install via NuGet/CLI download; release automation in place.

## Phase 8 – Documentation & Community

- **Deliverables**
  - Complete “Getting Started” chapters for common workflows (CLI, ASP.NET Core integration, CI pipelines).
  - Add migration authoring best practices with examples.
  - Record architecture diagrams and exemplary PR templates.
  - Launch discussion forum or GitHub Discussions for support.
- **Dependencies**: Major features implemented (Phases 1–7).
- **Success Criteria**: Contributors follow documented processes; community resources live.

## Phase 9 – Parity Validation & Hardening

- **Deliverables**
  - Conduct full parity audit against golang-migrate features and edge cases.
  - Run soak tests performing large migration sequences and concurrent runners.
  - Address performance hotspots; benchmark against baseline (e.g. migration throughput).
  - Declare v1 readiness with stability guarantees.
- **Dependencies**: All earlier phases substantially complete.
- **Success Criteria**: Nika meets mission statement expectations; benchmarks and soak tests pass; v1 released.

## Ongoing Initiatives

- Track upstream golang-migrate changes and decide on adoption strategy.
- Expand driver/source ecosystem via community contributions.
- Maintain CI matrix across supported runtimes and database versions.
- Continuously improve documentation, samples, and developer tooling.

Refer to this roadmap during planning cycles and update as priorities evolve. Each phase should result in tangible value while preserving Nika’s commitment to predictable, explicit, and reliable migrations.
