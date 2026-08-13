# ADR-019: Production Envelope Encryption

## Status

Accepted

## Context

EMF protects evidence and other potentially sensitive content that may be
large enough that sending the content through an external key-management
service for every encryption operation would be inefficient.

Production key-management systems may also use non-exportable keys.

EMF therefore requires a design that separates bulk content encryption from
key protection.

## Decision

EMF production content encryption will use envelope encryption.

A randomly generated symmetric Data Encryption Key (DEK) will be used for
bulk content encryption.

The DEK will encrypt the content using an authenticated encryption algorithm,
such as AES-GCM.

A provider-managed Key Encryption Key (KEK) will protect the DEK using a
supported key-wrapping operation.

The KEK remains under control of the external key-management provider and is
not exported to EMF.

Encrypted content metadata will retain the information required to recover
the DEK, including:

- encrypted DEK
- KEK identifier
- encryption algorithm
- nonce or initialization vector
- authentication tag
- any required authenticated associated data

The EMF security contracts will remain provider-neutral.

Vendor-specific key-management implementations will be isolated in adapter
projects outside the core EMF.Security contracts.

## Key Rotation

New content will use the current KEK.

Previously encrypted content will retain the KEK identifier and encrypted DEK
required for decryption.

Historical KEK versions must remain available for decryption for as long as
the associated encrypted content must remain accessible.

KEK rotation therefore does not require immediate re-encryption of all
existing content.

A future re-wrapping operation may replace the encrypted DEK with one protected
by a newer KEK without decrypting and re-encrypting the underlying content.

## Consequences

Benefits:

- large content can be encrypted locally without sending the content to the
  key-management service
- production KEKs can remain non-exportable
- key rotation does not require bulk content re-encryption
- historical encrypted content remains decryptable
- cloud and HSM providers can be isolated behind provider adapters
- EMF remains portable across deployment environments

Tradeoffs:

- encrypted content requires additional metadata
- DEKs exist temporarily in application memory during encryption and
  decryption
- key wrapping and unwrapping introduce provider operations
- provider-specific integration testing is required
- key lifecycle and retention policies must preserve required historical KEKs

## Security Considerations

DEKs must be generated using a cryptographically secure random number
generator.

DEKs must never be logged.

Plaintext content and unprotected DEKs must not be persisted together.

KEKs must never be persisted as raw key material by EMF.

The encrypted DEK must be authenticated as part of the encrypted-content
metadata or protected through an equivalent authenticated envelope.

Authorization to use a KEK remains separate from content protection
classification and authorization policy.

The system must fail closed when the required KEK or encrypted DEK cannot be
retrieved or unwrapped.

