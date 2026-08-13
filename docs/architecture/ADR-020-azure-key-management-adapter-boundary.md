# ADR-020: Azure Key Management Adapter Boundary

## Status

Accepted

## Context

EMF supports provider-neutral cryptographic contracts while production
deployments may use Azure Key Vault or Azure Managed HSM for key management.

Azure-specific SDK types and dependencies must not become part of the
provider-neutral EMF.Security contracts or domain models.

## Decision

Azure key-management integration will be implemented as an adapter outside
the provider-neutral EMF.Security contracts.

The Azure adapter will:

- use Azure Key Vault or Managed HSM for key-management operations
- keep Azure SDK dependencies isolated to the adapter
- use provider-managed non-exportable KEKs
- wrap and unwrap EMF data-encryption keys through provider operations
- preserve the KEK identifier required for historical decryption

The adapter will not expose Azure SDK types through EMF.Security interfaces,
domain models, or platform contracts.

Azure configuration, authentication, endpoint selection, and credential
management remain infrastructure concerns.

## Consequences

Benefits:

- EMF.Security remains provider-neutral
- Azure can be replaced without changing domain contracts
- production KEKs remain outside application-controlled key material
- Azure authentication remains an infrastructure concern
- Azure-specific integration testing is isolated

Tradeoffs:

- an additional adapter project is required
- Azure integration requires provider-specific testing
- deployment configuration must supply appropriate Azure credentials
- Azure service availability becomes a production infrastructure dependency

## Security Considerations

Azure credentials must not be persisted in source code or application
configuration committed to the repository.

The adapter must not export or log KEK material.

Access to Key Vault or Managed HSM must follow least-privilege principles.

Historical KEK versions must remain available for encrypted content for as
long as retention policy requires decryption.

The adapter must fail closed when required key-management operations cannot
be completed.

