# ADR-014: Domain Extension Persistence Boundary

- Status: Accepted
- Date: 2026-08-11

## Context

EMF separates platform capabilities, Domain Extensions, and the Intelligence
Services Layer.

The platform owns persistence infrastructure and platform concepts such as
Evidence, workflow definitions, workflow executions, checkpoints, and status
transitions.

Domain Extensions own domain-specific concepts and rules. The Veterans Claims
Domain Extension defines concepts such as Veteran, Claim, Claim Issue,
Submission, Service Event, Exposure, Finding, Issue Decision, Disability
Evaluation, and Effective Date.

ADR-012 prohibits platform projects from depending on Domain Extension types.
ADR-013 prohibits the Veterans Claims Domain Extension from creating separate
evidence, provenance, workflow, integrity, or persistence infrastructure.

The Veterans Claims domain nevertheless requires durable storage for its own
records and relationships.

## Decision

Domain Extensions own the repository contracts and persistence semantics for
their domain concepts.

Provider-specific implementations shall reside in adapter projects outside
both the platform persistence project and the pure domain-model project.

The Veterans Claims SQLite adapter project will be:

`EMF.Extensions.VeteransClaims.Persistence.Sqlite`

The dependency direction will be:

- `EMF.Core` does not depend on a Domain Extension.
- `EMF.Persistence` does not depend on a Domain Extension.
- `EMF.Extensions.VeteransClaims` may depend on `EMF.Core`.
- the SQLite adapter may depend on the Veterans Claims extension,
  `EMF.Core`, and `EMF.Persistence`
- application composition may depend on the platform and selected adapters

Veterans Claims repository interfaces belong to the Veterans Claims Domain
Extension because their operations and consistency boundaries are
domain-specific.

The SQLite adapter owns the schema and migrations for Veterans Claims tables.
Domain table names must be scoped to avoid collisions with platform tables and
other Domain Extensions.

The adapter may use the same configured SQLite database as platform
persistence. Sharing a database does not transfer ownership of domain semantics
to the platform.

Veterans Claims persistence shall reference platform Evidence through stable
platform identities such as `ArtifactId`. It shall not duplicate platform
Evidence content, provenance, integrity records, or storage behavior.

Persistence will be introduced incrementally by aggregate or coherent
transactional boundary. Provider-neutral domain services shall depend on
domain repository contracts, not SQLite implementation types.

## Initial Persistence Slice

The first slice will persist a Veteran aggregate root sufficient to verify:

- creation and retrieval by `VeteranId`
- stable identity round-trip
- schema initialization and repeatable initialization
- isolation from platform-owned persistence tables
- use through a Veterans Claims repository contract

Additional aggregates and relationships will be added after their consistency
and transaction boundaries are understood.

## Consequences

Benefits:

- platform projects remain independent of Veterans Claims types
- Veterans Claims retains ownership of its persistence semantics
- provider-specific storage remains outside the pure domain model
- platform Evidence remains authoritative and is referenced rather than copied
- additional providers can implement the same domain contracts
- persistence can grow incrementally with tested aggregate boundaries

Tradeoffs:

- an additional adapter project is required
- application composition must configure domain persistence adapters
- cross-boundary transactions require explicit coordination
- schema migration ownership must remain clear
- repository boundaries must be deliberately designed

## Rejected Alternatives

### Put Veterans Claims repositories in EMF.Persistence

Rejected because a platform project would depend on a specific Domain
Extension, reversing the dependency established by ADR-012.

### Put SQLite code in EMF.Extensions.VeteransClaims

Rejected because the pure domain model would become coupled to a storage
provider.

### Store domain objects as platform Evidence

Rejected because domain state and relationships are not interchangeable with
Evidence.

### Create an independent Veterans Claims evidence store

Rejected because ADR-013 requires the domain to reference platform Evidence
rather than duplicate it.

## Architectural Principle

The platform owns persistence capabilities and platform data semantics.

Domain Extensions own the persistence contracts and semantics of their domain
records.

Provider adapters connect those boundaries without reversing dependencies.
