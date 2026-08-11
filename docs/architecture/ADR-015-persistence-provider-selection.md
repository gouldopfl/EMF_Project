# ADR-015: Persistence Provider Selection and Composition

- Status: Accepted
- Date: 2026-08-11

## Context

EMF must support multiple persistence providers without coupling platform or
Domain Extension behavior to a specific database.

SQLite is the first implemented provider for platform persistence and the
Veterans Claims Domain Extension. Future environments may require PostgreSQL,
SQL Server, cloud-native stores, or other providers.

Repository contracts are provider-neutral, while implementations such as
`SqliteVeteranRepository` and `SqliteVaDecisionRepository` are intentionally
provider-specific.

The current application does not yet have a dependency-injection or
configuration convention for selecting persistence implementations.

A provider-selection boundary is required before adding a second database
implementation.

## Decision

Persistence provider selection belongs to the application composition root.

Domain services, workflows, and other provider-neutral components shall depend
on repository contracts rather than concrete persistence classes.

Each persistence provider shall supply a separate adapter package containing
its provider-specific repositories, schema management, and registration or
factory behavior.

The composition root shall select the configured provider during application
startup and supply the corresponding implementations to provider-neutral
consumers.

Provider selection shall be configuration-driven. Environment-specific
configuration may choose the provider and supply its connection settings.

Unknown, unavailable, or incomplete provider configuration shall fail during
startup rather than during a domain operation.

The provider is selected once for an application composition. Repositories
shall not switch database providers during an operation.

A provider-neutral consumer may receive contracts such as:

- `IVeteranRepository`
- `IClaimRepository`
- `ISubmissionRepository`
- `IVaDecisionRepository`
- `IDisabilityEvaluationRepository`

A SQLite composition supplies implementations from
`EMF.Extensions.VeteransClaims.Persistence.Sqlite`.

A future PostgreSQL composition would supply implementations from a separate
PostgreSQL adapter without changing the consuming domain services.

The composition mechanism may initially use an explicit factory and may later
integrate with a dependency-injection container. The architectural requirement
is dependency inversion and provider selection at composition time, not a
particular container framework.

Connection strings, credentials, endpoints, and provider-specific options
belong to application configuration and shall not be embedded in domain
objects or repository contracts.

## Consequences

Benefits:

- domain and workflow code remain database-neutral
- providers can evolve and be tested independently
- deployment environments can select appropriate storage
- unsupported configuration fails early
- adding a provider does not require modifying domain services
- provider-specific schema and behavior remain isolated

Tradeoffs:

- each provider requires its own adapter and integration tests
- application startup must compose a complete, compatible provider set
- schema migration behavior must be implemented per provider
- cross-provider migration requires explicit tooling or workflows
- configuration validation becomes an application responsibility

## Rejected Alternatives

### One universal repository adapter with database switches

Rejected because provider-specific branches would spread through repository
operations, combine unrelated database behavior, and make each new provider
modify existing adapter code.

### Select the provider inside domain services

Rejected because domain behavior must not depend on deployment infrastructure
or environment configuration.

### Instantiate SQLite repositories throughout application code

Rejected because callers would become coupled to the first provider and future
provider selection would require widespread changes.

### Switch providers during a repository operation

Rejected because it would make transaction ownership, consistency, failure
handling, and audit behavior ambiguous.

## Architectural Principle

Contracts define what persistence capabilities the consumer requires.

Provider adapters implement those capabilities.

The application composition root selects and configures the provider.
