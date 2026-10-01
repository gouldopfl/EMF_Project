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



## Accepted Amendment — 2026-10-01: Typed Provider Failure Sanitization

**Status:** Accepted
**Date:** 2026-10-01

This dated amendment extends the accepted decision. Original text above is preserved as architectural history. Where explicitly clarified below, this accepted amendment governs the current architecture; unrelated original decisions remain in force.

### Cross-ADR identities and dependency direction

Core-facing contracts MUST use Core-owned provider-neutral identity/value representations for operation, receipt, ownership, classification reference/revision and audit-obligation identity where those values cross the Core boundary. EMF.Core MUST NOT reference EMF.Security or its concrete models. Security owns authorization, classification authority, canonical audit-event models and lifecycle policy, and maps those models to the neutral contract representations. AuditEventId, ClassificationRevision, OperationId, Receipt and OwnershipToken retain their distinct validated identity semantics across that mapping; shared spelling or representation does not make them interchangeable. SQLite catalog and Azure SDK types remain implementation details outside Core.

Physical Revision identifies a store-issued object generation; provisional OwnershipToken establishes lifecycle ownership; mutation OperationId identifies a logical mutation and Receipt records its durable outcome; ClassificationRevision identifies authoritative classification state; AuditEventId identifies one canonical audit event. These are separate typed concepts. None silently substitutes for another. Identifiers are bounded, validated, non-sensitive values and contain no plaintext, wrapped key bytes, credentials or DEK/KEK material. An ownership token is an opaque coordination identifier, not a bearer authorization credential; authorization is independently required.

No Azure SDK types enter EMF.Core or provider-neutral EMF.Security contracts. Physical stores remain cryptography-unaware. Provider failures follow ADR-020 typed sanitized model. Recovery is authorized under its service identity, preserves the original actor separately, and is audited through ADR-049. Deployment policy, classification governance, recovery schedules, historical-key retention and alert escalation remain deployment obligations.

### Amendment scope and decision

Extend the Azure adapter failure boundary while preserving provider-managed non-exportable KEKs, historical identity, authentication/configuration ownership and Azure SDK isolation. No demonstrated production secret leakage is asserted; current raw exception propagation motivates an explicit outward policy.

Define neutral categories in EMF.Security: KeyUnavailable, AccessDenied, ProviderUnavailable, Throttled, InvalidEnvelope, AuthenticationFailed, UnsupportedAlgorithm, KeyVerificationFailed and UnknownProviderFailure. Preserve distinct historical-key absence vs ciphertext authentication failure. Category is based on operation and structured evidence, not speculation.

Outward failure contains only operation category, stable failure category, approved provider identity, retryability Yes/No/Unknown and bounded correlation ID. Its message is generated from fixed safe templates. No arbitrary diagnostics, request/response body, credentials, token, plaintext, DEK/KEK, wrapped-key bytes, URL/path or raw exception payload is attached. Do not attach raw provider exception as outward InnerException.

Azure adapter translates typed SDK exceptions and structured status/error codes. Do not parse English messages for security decisions. Neutral boundary translates unexpected provider exceptions to UnknownProviderFailure; retryability is Unknown unless reliably established. Preserve cancellation as OperationCanceledException/cancellation, including token semantics, rather than misreporting it as provider unavailability.

Correlation IDs are EMF-generated or explicitly validated as bounded opaque provider metadata against an allowlist grammar/length. Never copy arbitrary provider diagnostics into correlation fields. Audit/log facts use explicit allowlists with length validation. Identifiers such as key name/version must themselves be validated before appearing in bounded schemas.

Raw originals, if retained at all, belong only in a separately access-controlled diagnostic facility with a defined redaction, retention and retrieval policy. They do not enter ordinary logs/audits, outward exceptions or telemetry serialization by default. An internal handle may link to a validated correlation ID; absence of this facility means originals are not retained.

### Affected current files

- src/EMF.Security.Azure/Cryptography/AzureCryptographyClientAdapter.cs — SDK call translation at wrap/unwrap boundary.
- src/EMF.Security.Azure/Cryptography/AzureKeyVaultCryptography.cs — safe adapter contract propagation.
- src/EMF.Security.Azure/Cryptography/AzureKeyCryptographyFactory.cs and Clients/AzureKeyVaultClientFactory.cs — configuration/client construction failures sanitized before escape.
- src/EMF.Security.Azure/Keys/ConfiguredAzureKeyReferenceProvider.cs — key identity lookup/validation failure mapping.
- src/EMF.Security.Azure/Encryption/AzureEnvelopeEncryptionService.cs and AzureEnvelopeKeyRewrappingService.cs — distinct content-authentication and key-provider categories; buffer cleanup preserved.
- src/EMF.Security/Encryption/Envelope/Services/DevelopmentEnvelopeEncryptionService.cs and DevelopmentEnvelopeKeyRewrappingService.cs — same neutral outward semantics for test/development providers.
- src/EMF.Security/Storage/ArtifactEnvelopeRewrappingService.cs — bounded sanitized failure audit facts.
- src/EMF.Console/SecurityConsoleCommand.cs and VeteransConsoleCommand.cs — print only safe outward errors; no raw exception formatting from this boundary.

### Verification, migration and consequences

Preserved synthetic-marker regression exercises outward containment, not proof of real leakage. Test typed mock SDK failures, nested sensitive marker exclusion including ToString/InnerException, cancellation, stable categories, retryability Unknown, invalid correlation metadata and audit/log allowlists. Unit validation never uses live Azure credentials/services.

Introduce neutral failure model before adapters/callers migrate; retain operational diagnosis through safe category/correlation, not raw diagnostics. Exact structured-code mappings require provider-specific review and tests; unrecognized codes remain UnknownProviderFailure. Deployment must approve any raw diagnostic facility separately. References: ADR-018/019/020/022/032 and ADR-049.

### Related architecture decisions

- [ADR-018](ADR-018-security-key-management-boundary.md)
- [ADR-019](ADR-019-production-envelope-encryption.md)
- [ADR-022](ADR-022-artifact-envelope-key-rewrapping-lifecycle.md)
- [ADR-032](ADR-032-security-alert-detection-boundary.md)
- [ADR-049](ADR-049-security-sensitive-mutation-audit-delivery.md)
