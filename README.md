# Nika

Nika is a C# database migration toolkit modeled after the battle-tested [golang-migrate](https://github.com/golang-migrate/migrate). It delivers a predictable way to evolve database schemas through a composable library and a thin CLI wrapper that share the same execution guarantees.

## Why Nika

- **Proven philosophy** – embraces golang-migrate’s mission of dumb drivers, smart core, and explicit failure handling.
- **.NET-first experience** – exposes idiomatic APIs for background services and automation workflows while retaining CLI parity with the original project.
- **Deterministic migrations** – sequences migrations in strict order, enforces dirty-state checks, and leverages datastore-native locking where available.
- **Productive tooling** – the CLI now supports `up`, `down`, `goto`, `drop`, `force`, `steps`, `version`, and `create` commands for day-to-day workflows.
- **Multi-database drivers** – first-party drivers ship for PostgreSQL and SQL Server, each with container-backed integration tests for easy validation.

Read the full vision in [`docs/mission-statement.md`](docs/mission-statement.md) and the architectural guardrails in [`docs/implementation-philosophy.md`](docs/implementation-philosophy.md).

## Project Layout (planned)

- `src/Nika` – core orchestration library
- `src/Nika.Cli` – cross-platform CLI wrapper
- `src/Nika.Database.*` – first-party database drivers
- `src/Nika.Source.*` – migration source adapters
- `tests` – unit, integration, and docker-backed end-to-end suites

## Documentation

- Documentation Index – [`docs/README.md`](docs/README.md)
- Mission & Philosophy – see the documents linked above.
- CLI Quickstart – [`docs/cli-quickstart.md`](docs/cli-quickstart.md)
- Driver & Source Authoring – [`docs/driver-and-source-guide.md`](docs/driver-and-source-guide.md)
- Technical Baseline – [`docs/technical-baseline.md`](docs/technical-baseline.md)
- PostgreSQL Driver (preview) – configure via `driver.name = "postgres"` in `nika.config.json`

## Roadmap Highlights

- Follow the phased implementation plan in [`docs/roadmap.md`](docs/roadmap.md).
- Current focus: foundation scaffolding, core engine prototype, filesystem source, and PostgreSQL driver.
- Upcoming: CLI parity, additional drivers/sources, observability tooling, packaging, and community launch.

## Contributing

We welcome contributions that align with the mission and implementation philosophy. Start by reading [`CONTRIBUTING.md`](CONTRIBUTING.md), open an issue to discuss design changes, and keep feature parity with golang-migrate front of mind.
