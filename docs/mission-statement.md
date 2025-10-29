# Nika Mission Statement

Nika is a C# database migration toolkit inspired by the proven design of `golang-migrate`. Our mission is to provide developers with a dependable, composable way to evolve database schemas with confidence—whether they automate migrations in CI/CD pipelines or drive them from rich backend services.

## Purpose

- Deliver a bulletproof migration engine that applies schema changes in the intended order, guards against corruption, and makes it trivial to reason about the state of a database.
- Offer first-class ergonomics for both CLI users and .NET developers embedding Nika into their applications.
- Ensure parity with `golang-migrate` semantics so teams can adopt Nika without relearning migration discipline.

## Core Commitments

- **Deterministic execution** – migrations apply sequentially with predictable ordering, version tracking, and locking to prevent concurrent drift.
- **Explicit failures** – when in doubt, Nika fails fast and surfaces actionable errors instead of guessing the user’s intent.
- **Dumb drivers, smart core** – database and source drivers are intentionally lightweight. The orchestration logic lives in Nika’s core so every integration benefits from the same safety guarantees.
- **Low operational friction** – the CLI is a thin wrapper around the library, with transparent configuration and graceful handling of interrupts.
- **Resource-conscious design** – Nika streams migration bodies and keeps runtime memory usage bounded, even for large migration repositories.

## What We Build

- **Library-first foundation** – a stable, versioned .NET API that maps closely to `golang-migrate`'s package surface: migration factories, runner abstractions, graceful shutdown mechanisms, and pluggable logging.
- **_First-party drivers_** – canonical implementations for popular relational and document databases that stick to the contract: run the migration text as provided, respect transactions when available, and report dirty states.
- **Migration sources** – filesystem, embedded resources, and remote source adapters that focus solely on retrieving ordered migration pairs.
- **Cross-platform CLI** – a self-contained tool that mirrors the library experience, offering the core commands (`up`, `down`, `force`, `goto`, `version`, `drop`) without hidden configuration.

## How We Work

- Keep feature parity with `golang-migrate` front of mind; divergence requires clear justification and documentation.
- Design APIs that make idempotent and reversible migrations the path of least resistance.
- Favor simple, audited code paths over clever abstractions—clarity is a prerequisite for reliability.
- Treat documentation and examples as part of the product so new contributors emulate the intended migration discipline.

## Definition of Success

- Teams entrust Nika with production schema changes because it is predictable, well-documented, and battle-tested.
- Database and source driver authors find it easy to contribute new integrations that behave consistently with the core philosophy.
- Users moving between `golang-migrate` and Nika recognize familiar workflows, command semantics, and failure handling.
