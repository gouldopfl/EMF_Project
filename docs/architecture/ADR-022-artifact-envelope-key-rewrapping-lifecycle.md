# ADR-022: Artifact Envelope Key Rewrapping Lifecycle

## Status

Accepted

## Context

ADR-018 establishes provider-neutral key management.

ADR-019 establishes envelope encryption and permits a future operation that
rewraps a data-encryption key under a newer key-encryption key without
decrypting and re-encrypting the underlying content.

ADR-021 establishes encrypted artifact content storage through a storage
decorator.

Current envelope-encryption services can encrypt new content and decrypt
historical content using the key identifier stored in each envelope.

Historical-key lookup preserves access after key rotation, but encrypted
artifact envelopes remain dependent on their original key-encryption keys
until their wrapped data-encryption keys are updated.

Key retirement, compromise response, retention policy, or provider migration
may require selected artifact envelopes to be rewrapped under the current
key-encryption key.

## Decision

EMF will represent key rewrapping as a separate, provider-neutral capability.

The envelope rewrapping contract shall:

- accept an existing encrypted envelope
- unwrap its data-encryption key using the historical key-encryption key
- wrap that same data-encryption key using the current key-encryption key
- return a replacement envelope
- preserve ciphertext, nonce, authentication tag, and content algorithm
- avoid decrypting or re-encrypting artifact content

Rewrapping shall not be added to `IEnvelopeEncryptionService`.

Providers that support rewrapping will implement the separate capability.
Providers that do not support it may continue to support encryption and
decryption without advertising rewrapping support.

## Artifact Rewrapping

Artifact rewrapping shall operate through a security-layer lifecycle service,
not through Orchestration, Domain Extensions, or the physical content-storage
provider.

The lifecycle service shall:

- identify the artifact content to update
- read and validate the serialized encrypted envelope
- request envelope rewrapping from the configured provider
- replace the stored envelope without changing the Artifact identity
- preserve the original envelope when rewrapping or replacement fails
- verify that the replacement envelope remains decryptable
- report whether the artifact was updated or already used the current key

The plaintext content fingerprint, Artifact metadata, Evidence provenance, and
domain relationships shall not change because an envelope is rewrapped.

A single-artifact rewrapping operation is the initial consistency boundary.

Bulk rotation may coordinate multiple single-artifact operations, but it shall
not create an all-or-nothing transaction across an entire content store.

## Failure and Recovery

Rewrapping shall fail closed.

Failure to retrieve either the historical or current key shall leave the
stored envelope unchanged.

Failure to unwrap, wrap, serialize, validate, or replace an envelope shall not
remove the previously readable envelope.

Cancellation shall not be reported as successful rewrapping.

A retry may safely reevaluate the stored key identifier and continue from the
current durable envelope.

Physical content stores used for rewrapping must provide replacement semantics
that do not expose a partially written envelope.

The rewrapping provider shall verify that the newly wrapped data-encryption key
represents the same data-encryption key before the replacement envelope is
stored. This verification does not require decrypting the artifact ciphertext.

## Security and Audit

Rewrapping is a security-relevant operation and must be subject to applicable
authorization policy.

The operation must expose facts required for audit, including:

- the Artifact identity
- the previous and replacement key-encryption key identifiers
- the acting identity or execution context
- the operation time
- whether the operation updated, skipped, or failed

Audit information shall not include plaintext content, raw data-encryption
keys, or key-encryption key material.

Historical keys shall not be retired until policy confirms that no retained
content still depends on them.

## Verification Requirements

Tests shall verify:

- rewrapping preserves encrypted artifact ciphertext
- rewrapping preserves nonce, authentication tag, and algorithm
- rewrapping changes the wrapped data-encryption key and key identifier
- the rewrapped envelope decrypts to the original plaintext
- already-current envelopes are not unnecessarily replaced
- a missing historical key leaves stored content unchanged
- a wrapping failure leaves stored content unchanged
- a storage replacement failure preserves the previous durable envelope
- corruption is rejected rather than rewrapped
- cancellation is propagated

## Consequences

Benefits:

- historical key-encryption keys can be retired deliberately
- large artifact content does not require bulk decryption and re-encryption
- Artifact identity, provenance, and integrity semantics remain stable
- provider-specific key operations remain outside platform and domain layers
- rotation can proceed incrementally and recoverably

Tradeoffs:

- providers need an additional optional capability
- lifecycle coordination and audit records are required
- historical and current keys may both be required during rewrapping
- physical stores must support safe replacement
- bulk rotation progress requires separate durable coordination

## Rejected Alternatives

### Add rewrapping to IEnvelopeEncryptionService

Rejected because encryption and decryption are baseline capabilities while
rewrapping is an optional key-lifecycle capability.

### Decrypt and re-encrypt all artifact content

Rejected because it unnecessarily processes plaintext and changes ciphertext,
nonce, and authentication metadata.

### Rewrap automatically during every read

Rejected because reads would unexpectedly mutate durable security state and
could introduce provider, storage, authorization, and audit failures into a
read operation.

### Require one transaction across the entire content store

Rejected because physical content stores may not support global transactions
and one failure would prevent incremental progress.

## Architectural Principle

Key rotation changes protection metadata without changing the protected
evidence.

EMF rewraps envelope keys through explicit, authorized, auditable, and
recoverable lifecycle operations.


## Accepted Amendment — 2026-10-01: Authenticated Artifact Envelope Key Rewrapping

**Status:** Accepted
**Date:** 2026-10-01

This dated amendment extends the accepted decision. Original text above is preserved as architectural history. Where explicitly clarified below, this accepted amendment governs the current architecture; unrelated original decisions remain in force.

### Cross-ADR identities and dependency direction

Core-facing contracts MUST use Core-owned provider-neutral identity/value representations for operation, receipt, ownership, classification reference/revision and audit-obligation identity where those values cross the Core boundary. EMF.Core MUST NOT reference EMF.Security or its concrete models. Security owns authorization, classification authority, canonical audit-event models and lifecycle policy, and maps those models to the neutral contract representations. AuditEventId, ClassificationRevision, OperationId, Receipt and OwnershipToken retain their distinct validated identity semantics across that mapping; shared spelling or representation does not make them interchangeable. SQLite catalog and Azure SDK types remain implementation details outside Core.

Physical Revision identifies a store-issued object generation; provisional OwnershipToken establishes lifecycle ownership; mutation OperationId identifies a logical mutation and Receipt records its durable outcome; ClassificationRevision identifies authoritative classification state; AuditEventId identifies one canonical audit event. These are separate typed concepts. None silently substitutes for another. Identifiers are bounded, validated, non-sensitive values and contain no plaintext, wrapped key bytes, credentials or DEK/KEK material. An ownership token is an opaque coordination identifier, not a bearer authorization credential; authorization is independently required.

No Azure SDK types enter EMF.Core or provider-neutral EMF.Security contracts. Physical stores remain cryptography-unaware. Provider failures follow ADR-020 typed sanitized model. Recovery is authorized under its service identity, preserves the original actor separately, and is audited through ADR-049. Deployment policy, classification governance, recovery schedules, historical-key retention and alert escalation remain deployment obligations.

### Amendment scope

This accepted amendment clarifies the historical prohibition on content decryption in Decision and the corresponding Context/Consequences language. The optional capability, same-DEK rotation, stable Artifact/provenance, and no content re-encryption decisions remain. Replacement-DEK verification and existing-content authentication are separate required checks. The original wording remains as decision history; the precise authentication exception below governs current architecture.

### Decision

Rewrapping does not expose, persist, use for application/business purposes, or re-encrypt artifact plaintext. The encryption provider may transiently decrypt ciphertext solely to authenticate existing AEAD ciphertext and Artifact-bound authenticated context before any AlreadyCurrent or rewrap decision. Transient plaintext remains private to the cryptographic provider and is cleared promptly.

The currently used AES-GCM API has no authenticate-only operation. Provider-internal bounded decryption is permitted; custom or duplicate GCM code in the lifecycle is prohibited. Ciphertext, nonce, authentication tag, content algorithm, authenticated metadata and DEK remain unchanged. Only wrapped-DEK protection metadata and its KEK identity change.

Prefer one separate context-aware authenticated-rewrap provider capability, not separate lifecycle authenticate/unwrap/wrap calls and not an addition to baseline IEnvelopeEncryptionService:

```csharp
Task<AuthenticatedRewrapResult> RewrapAuthenticatedAsync(
    EncryptedEnvelope envelope,
    ReadOnlyMemory<byte> authenticatedContext,
    CancellationToken cancellationToken);
```

Result contains disposition, previous/current KEK identities and replacement envelope; no plaintext or raw DEK. Shared Security-owned Artifact context construction must match EncryptedArtifactContentStore exactly. Providers share existing AES-GCM/AAD implementation internally rather than copying it into the lifecycle.

### Required ordering and buffer ownership

1. Resolve actual Artifact/classification and authorize using ADR-017/031.
2. Read exact serialized bytes plus opaque Revision using ADR-047.
3. Validate supported format/algorithm, bounded sizes and Artifact-bound context.
4. Resolve stored historical KEK identity, unwrap DEK, verify identity and DEK length.
5. Authenticate all ciphertext, nonce/tag and reconstructed AAD with that DEK. Wrong Artifact binding is AuthenticationFailed.
6. Clear private transient plaintext immediately after authentication, in finally; never return, log, audit or persist it. Size is bounded by a configured validated plaintext limit before allocation. Provider owns the entire buffer; if pooled, clear the whole sensitive lease before return. No transient plaintext is retained across subsequent asynchronous key calls.
7. Resolve/select current KEK and only now decide AlreadyCurrent. Even AlreadyCurrent requires successful authentication and no unnecessary physical replacement.
8. If needed, wrap the same DEK with current KEK, unwrap the replacement and compare in constant time. Zero DEK and comparison buffers in finally on every path.
9. Validate immutable envelope fields and conditionally promote against the exact Revision originally read; conflict preserves the newer object. No unsafe capability fallback.
10. Record durable mutation/audit result through ADR-049.

All pre-promotion failures preserve durable bytes. Missing historical key and AuthenticationFailed are distinct sanitized categories. Cancellation before promotion propagates safely. Cancellation or audit failure after known durable promotion must preserve the committed outcome rather than pretend no mutation occurred; ADR-049 governs reconciliation.

### Affected current files

- src/EMF.Security/Encryption/Envelope/IEnvelopeKeyRewrappingService.cs — capability evolution.
- src/EMF.Security/Encryption/Envelope/IEnvelopeEncryptionService.cs — shared internal implementation, no rewrap added to baseline interface.
- src/EMF.Security/Encryption/Envelope/EncryptedEnvelopeFormat.cs — reuse format/AAD construction.
- src/EMF.Security/Encryption/Envelope/Services/DevelopmentEnvelopeEncryptionService.cs and DevelopmentEnvelopeKeyRewrappingService.cs — provider-internal authentication and buffer ownership.
- src/EMF.Security.Azure/Encryption/AzureEnvelopeEncryptionService.cs and AzureEnvelopeKeyRewrappingService.cs — equivalent provider operation with adapter-only key management.
- src/EMF.Security/Storage/EncryptedArtifactContentStore.cs:120 — centralize identical Artifact context semantics.
- src/EMF.Security/Storage/ArtifactEnvelopeRewrappingService.cs:183,233,253 — authenticated operation, authenticated skip, conditional promotion.

### Verification and consequences

Preserved four ciphertext/tag corruption regressions and wrong-Artifact regression must pass. Add meaningful nonce/algorithm/size failure coverage, assertions that failure precedes replacement wrapping, current-envelope authentication, immutable ciphertext/AAD checks, same-DEK verification, cancellation and prompt buffer cleanup. Existing missing-historical-key byte-for-byte preservation remains a passing guard.

Authentication needs memory proportional to bounded ciphertext size and makes historical-key availability necessary even for current-key skips. Streaming/custom GCM is not authorized by this amendment. No alternative allowing unauthenticated AlreadyCurrent is acceptable. References: ADR-018/019/020/021, ADR-047/049.

### Related architecture decisions

- [ADR-017](ADR-017-protected-regulated-information-boundary.md)
- [ADR-018](ADR-018-security-key-management-boundary.md)
- [ADR-019](ADR-019-production-envelope-encryption.md)
- [ADR-020](ADR-020-azure-key-management-adapter-boundary.md)
- [ADR-021](ADR-021-artifact-content-protection-boundary.md)
- [ADR-031](ADR-031-resource-neutral-authorization.md)
- [ADR-047](ADR-047-versioned-artifact-content-mutation.md)
- [ADR-049](ADR-049-security-sensitive-mutation-audit-delivery.md)
