# Nika Implementation Philosophy

This document captures the principles we inherit from `golang-migrate` and clarifies how they map onto the Nika codebase. Treat these as guardrails when designing APIs, reviewing contributions, or authoring new integrations.

## Architecture Overview

- **Core Orchestrator** – houses the migration runner, version tracking, and locking logic. It owns ordering, batching, and graceful interruption (e.g. `GracefulStop` channels in `golang-migrate`).
- **CLI Wrapper** – a thin host that wires configuration flags, surfaces errors verbatim, and handles signals such as `CTRL+C` without corrupting state.
- **Database Drivers** – adapters that execute raw migration bodies against a target datastore. Drivers must respect transactions when supported and set dirty states atomically around work units.
- **Source Drivers** – adapters that fetch ordered migration pairs (`*.up.*`, `*.down.*`) from filesystems, archives, embedded resources, or remote stores.

The repo structure mirrors `golang-migrate`:

```text
/                 Core library
/cli              CLI host
/database         Database driver implementations
/source           Migration source implementations
```

## Driver Contract

- Treat the Nika core as the single source of truth for sequencing and orchestration. Drivers should expose only the primitives needed to run statements and manage locks.
- Never mutate user-supplied migration bodies. Drivers must not inject guards such as `IF EXISTS` or reorder statements.
- Fail loudly on ambiguous input. For instance, surface connection-string parsing issues instead of compensating.
- Use datastore-native locking when available (e.g. PostgreSQL advisory locks, MySQL `GET_LOCK`) so concurrent runners coordinate rather than race.
- Report dirty state if a migration fails mid-flight; do not attempt automatic rollback beyond what the datastore guarantees.

## Migration Sources

- Provide ordered access to `up` and `down` migration files without interpreting their contents.
- Avoid preloading entire repositories when streaming suffices. Favor lazy enumeration to keep memory usage bound.
- Maintain parity with the source URL semantics used by `golang-migrate` (e.g. `file://`, `s3://`, `github://`) so existing workflows translate directly.

## Migration Authoring Guidelines

- Each logical migration consists of two files: `{version}_{slug}.up.{ext}` and `{version}_{slug}.down.{ext}`.
- Version identifiers are unsigned integers; prefer monotonically increasing sequences or timestamps.
- Encourage reversible, idempotent migrations. They simplify testing (`up` → `down` → `up`) and reduce deployment risk.
- Recommend wrapping multi-statement migrations in transactions when the database supports transactional DDL.

## Operational Guarantees

- Nika tracks the current version automatically in the target datastore. Users should not need to provision metadata tables manually.
- If a migration fails, the database is marked dirty and subsequent migrations are blocked until the user fixes the issue and calls `force` with the corrected version.
- Strongly suggest that users rely on datastores with locking support when running multiple migration processes simultaneously.

## Contributor Expectations

- Maintain parity with `golang-migrate` command set and flags unless divergence is documented and justified.
- Tests must exercise both `short` (unit-level) and integration paths across supported databases. Docker-based adapters mirror the upstream testing approach.
- Documentation changes accompany feature work; runbooks and examples are part of the deliverable, not afterthoughts.

By aligning on these principles, Nika inherits the battle-tested reliability of `golang-migrate` while giving .NET teams a familiar, maintainable migration workflow.
