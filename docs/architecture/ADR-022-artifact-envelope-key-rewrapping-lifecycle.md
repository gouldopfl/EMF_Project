# ADR-022: Artifact Envelope Key Rewrapping Lifecycle

## Status

Proposed

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
