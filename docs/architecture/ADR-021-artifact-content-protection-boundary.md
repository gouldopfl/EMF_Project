# ADR-021: Artifact Content Protection Boundary

## Status

Accepted

## Context

EMF discovers, inventories, and reasons about evidence represented by platform
Artifacts.

An Artifact contains platform metadata, identity, provenance, and integrity
information, but the underlying artifact content may itself contain protected
or regulated information.

ADR-017 establishes the boundary for protected and regulated information.
ADR-018 establishes provider-neutral security key management.
ADR-019 establishes envelope encryption for production content protection.
ADR-020 establishes Azure key management as an external adapter.

EMF therefore requires a clear boundary for storing artifact content without
coupling Core, Orchestration, Persistence, or Domain Extensions to a specific
encryption provider or cloud platform.

## Decision

Artifact content storage is represented by the provider-neutral
`IArtifactContentStore` contract in `EMF.Core`.

Physical content-storage implementations belong outside Core. The initial
filesystem implementation is provided by `EMF.Persistence` through
`FileSystemArtifactContentStore`.

Content protection is applied as a storage decorator rather than being
implemented directly by physical storage providers.

`EMF.Security` provides `EncryptedArtifactContentStore`, which decorates an
`IArtifactContentStore` and applies envelope encryption before content is
written and decryption after content is read.

Cloud-specific key management remains outside the provider-neutral security
layer. Production Azure deployments may compose
`EncryptedArtifactContentStore` with the Azure envelope-encryption
implementation defined by ADR-020.

`EMF.Orchestration` depends only on the Core artifact-content-storage contract.
It does not know whether content is stored on a filesystem, in another
persistence provider, encrypted, or protected by a particular key-management
system.

The application composition root is responsible for selecting and composing
the concrete content store and security implementation.

For an Azure-protected filesystem deployment, the runtime composition is:

    InventoryOrchestrationService
        |
        v
    EncryptedArtifactContentStore
        |
        v
    FileSystemArtifactContentStore

with envelope-encryption key operations supplied by `EMF.Security.Azure`.

## Consequences

Benefits:

- artifact content protection is independent of Domain Extensions
- Orchestration remains unaware of encryption implementation details
- physical storage providers do not need to implement cryptography
- encryption can be applied consistently through composition
- Azure-specific dependencies remain outside provider-neutral layers
- alternative content stores and key-management providers can be introduced
  without changing Core contracts
- artifact content can be protected while preserving existing Artifact
  identity, provenance, and integrity semantics

Tradeoffs:

- the application composition root must configure the storage and protection
  chain correctly
- encrypted content requires the corresponding key-management provider to be
  available for decryption
- deployments must manage content-storage locations independently from
  artifact metadata persistence
- key rotation and content re-encryption policies require separate lifecycle
  decisions

## Notes

This decision establishes the architectural boundary for artifact content
protection. It does not require all EMF deployments to use Azure or filesystem
storage.

Development and future deployment environments may provide other
`IArtifactContentStore` implementations or security decorators while
preserving the same platform boundary.
