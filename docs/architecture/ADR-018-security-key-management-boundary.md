# ADR-018: Security Key Management Boundary

## Status

Accepted

## Context

EMF requires encryption for protected content across multiple industries and
deployment environments.

Encryption keys may be managed by:

- local development infrastructure
- cloud key-management services
- hardware security modules
- enterprise key-management systems
- other approved cryptographic providers

The EMF security layer must not become dependent on a specific cloud provider,
operating system, or key-management implementation.

The development encryption implementation currently uses AES-GCM and obtains
raw key material through `IEncryptionKeyProvider`.

Production key-management systems may not permit raw key material to be
exported to application processes.

## Decision

EMF will maintain a provider-neutral key-management boundary.

`IEncryptionKeyProvider` provides:

- identification of the current encryption key
- retrieval of a specific key by identifier

The current contract supports the development encryption implementation and
historical key lookup required for key rotation.

Production implementations must not be required to expose raw key material
when the underlying key-management system does not permit it.

Production cryptographic implementations may therefore use provider-specific
cryptographic operations while remaining behind the EMF security boundary.

The EMF domain and platform layers will not depend directly on Azure Key Vault,
AWS KMS, an HSM, or another specific key-management product.

## Consequences

Benefits:

- EMF remains portable across operating systems.
- EMF remains portable across cloud providers.
- Key rotation can be supported without invalidating historical content.
- Development encryption can remain simple and testable.
- Production environments can use non-exportable keys.
- Industry-specific security requirements can be implemented without changing
  domain models.

Tradeoffs:

- Development and production encryption implementations may differ.
- Production providers may require provider-specific cryptographic operations.
- Additional integration testing will be required for each supported provider.
- Key lifecycle management remains an infrastructure responsibility.

## Security Considerations

Key identifiers may be persisted with encrypted content.

Raw key material must not be persisted as part of encrypted content.

Production key material must not be logged, serialized, or exposed through
general-purpose domain models.

Key rotation must preserve access to historical encrypted content for as long
as required by retention and legal policy.

Authorization and protection classification remain separate security
concerns from cryptographic key management.
