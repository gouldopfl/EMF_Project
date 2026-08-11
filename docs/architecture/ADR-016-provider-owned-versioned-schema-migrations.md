# ADR-016: Provider-Owned Versioned Schema Migrations

- Status: Proposed
- Date: 2026-08-11

## Context

EMF persistence adapters currently initialize schemas primarily through
`CREATE TABLE IF NOT EXISTS`.

Platform workflow persistence also adds missing columns by attempting an
`ALTER TABLE` statement and treating a duplicate-column error as evidence that
the migration was previously applied.

This approach is sufficient for early additive changes, but it does not record
which schema changes have been applied. It also becomes difficult to manage
ordered changes, transactional upgrades, incompatible database versions, and
migration failures as persistence schemas grow.

ADR-014 establishes that Domain Extensions own the persistence semantics of
their domain records and that provider adapters own their schemas and
migrations.

ADR-015 establishes that persistence providers are selected and configured by
the application composition root.

A durable migration mechanism must preserve those ownership and dependency
boundaries.

## Decision

Each persistence provider adapter shall own and execute the versioned
migrations for the schema it owns.

The Veterans Claims SQLite adapter shall maintain a provider-owned migration
ledger named:

`VeteransClaims_SchemaMigrations`

Each applied migration record shall contain:

- an integer version that uniquely orders the migration
- a stable migration name
- the UTC time at which the migration was applied

Migrations shall be ordered, append-only, and applied exactly once.

Migration execution shall follow these rules:

- pending migrations are applied in ascending version order
- each migration and its ledger entry are committed atomically
- repeated initialization does not reapply completed migrations
- a failed migration does not leave a completed ledger entry
- an adapter shall fail during startup when the database contains a schema
  version newer than the adapter supports
- provider-specific migration SQL remains inside the provider adapter
- migration ledgers must be scoped to their owning component

A fresh Veterans Claims database shall receive the initial schema as migration
version 1.

An existing Veterans Claims database created before migration tracking is
introduced may be baselined as version 1 only after the adapter verifies that
the expected version 1 schema is present.

## Agent-Owned Durable State

The same migration pattern applies to AI agents and other intelligent
components that own durable records.

A stateful agent or agent subsystem shall own:

- its repository contracts and persistence semantics
- its provider-specific schema and migrations
- a component-scoped migration ledger
- compatibility checks for its stored schema version

Agents sharing a physical database do not share ownership of tables or
migration history. Each agent or coherent agent subsystem migrates only the
schema objects it owns.

Stateless agents and agents that only consume platform or domain repositories
do not require a migration ledger.

Agent persistence providers shall be selected during application composition.
Provider selection or schema migration shall not occur during an agent
operation.

## Verification Requirements

Migration tests shall verify:

- initialization of a fresh database
- repeatable initialization
- ordered upgrade from an earlier schema version
- rollback when a migration fails
- rejection of a database newer than the adapter supports
- isolation between component-owned migration ledgers

## Consequences

Benefits:

- schema state is explicit and auditable
- upgrades occur in a deterministic order
- failed migrations can be rolled back atomically
- providers retain ownership of their schema behavior
- platform, domain, and agent schemas remain independently evolvable
- incompatible database versions fail during startup

Tradeoffs:

- each stateful provider adapter requires migration infrastructure
- existing databases require careful baseline verification
- migrations must remain immutable after release
- destructive or data-transforming changes require deliberate recovery plans
- shared databases contain multiple component-scoped migration ledgers

## Rejected Alternatives

### Maintain one migration ledger for the entire database

Rejected because a physical database may contain schemas owned by independent
platform components, Domain Extensions, providers, and agents.

### Continue relying only on CREATE TABLE IF NOT EXISTS

Rejected because it cannot express ordered schema evolution or prove which
changes have been applied.

### Detect migrations through expected database errors

Rejected as the primary migration mechanism because exception text is
provider-specific and does not provide an auditable migration history.

### Allow agents to modify domain or platform schemas

Rejected because agents do not own those persistence semantics and must use
the contracts exposed by the owning component.

### Select a provider during each operation

Rejected because provider selection and migration must be completed during
application startup to preserve stable transaction and failure behavior.

## Architectural Principle

Every stateful component owns the versioned evolution of its persistent
records.

Provider adapters perform migrations without transferring schema ownership
across platform, domain, or agent boundaries.
