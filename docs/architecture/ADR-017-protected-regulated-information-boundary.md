# ADR-017: Protected and Regulated Information Boundary

## Status

Accepted

## Context

EMF may process information subject to legal, regulatory, contractual, or
organizational protection requirements.

Examples include protected health information, personally identifiable
information, legal or privileged information, personnel information, and
other sensitive or regulated evidence.

ADR-012 assigns platform security and policy enforcement to the EMF platform
while Domain Extensions retain domain terminology and classifications.

ADR-013 establishes that domain Evidence Classification describes the role or
character of Evidence within a domain.

Domain evidence classification and information-protection classification are
therefore separate architectural concerns.

## Decision

EMF will establish a platform-level boundary for protected and regulated
information.

Protection requirements shall be represented and enforced through platform
security, policy, and information-governance mechanisms rather than embedded
within individual Domain Extensions.

Domain Extensions may identify information characteristics relevant to their
domain, but shall not independently implement fundamental access, disclosure,
encryption, audit, retention, or external-provider security mechanisms.

## Domain Classification and Protection Classification

Domain Evidence Classification and platform Protection Classification are
distinct concepts.

Domain classification describes the evidentiary role, character, or meaning
of Evidence within a Domain Extension.

Protection classification describes how information must be handled by the
platform.

A single Artifact may therefore have both domain classifications and platform
protection classifications without changing its identity, provenance, or
integrity semantics.

## Policy Enforcement

Protection policy may govern capabilities including:

- authorization and access
- disclosure
- data minimization
- storage and encryption
- transmission
- retention and deletion
- audit
- export
- external processing
- intelligence-provider eligibility

Protection decisions shall be enforced at platform boundaries rather than
left solely to presentation code or Domain Extension implementations.

Access to information does not automatically authorize disclosure of that
information to another user, system, service, or external provider.

## Intelligence Services Boundary

EISL remains the provider-neutral boundary for intelligence capabilities.

Protected information shall not be sent to an intelligence provider solely
because that provider supports the requested capability.

Provider selection must also satisfy applicable protection policy.

Protection policy may consider:

- permitted information classifications
- contractual authorization
- provider data retention and use
- deployment or geographic restrictions
- security capabilities
- applicable regulatory requirements

A Domain Extension shall not bypass EISL to transmit protected information
directly to an intelligence provider.

## Auditability

Security-relevant operations involving protected information must be capable
of producing auditable records.

Audit records should identify facts needed to reconstruct relevant activity,
including:

- the operation performed
- the resource involved
- the acting identity or execution context
- the applicable policy decision
- the destination or provider when information leaves EMF
- the time and outcome of the operation

Audit mechanisms should avoid unnecessarily duplicating protected content.

Security audit does not replace Evidence provenance. Provenance describes the
origin and history of Evidence; security audit describes security-relevant
operations involving information.

## Compliance Profiles and Deployment Responsibility

EMF may support compliance profiles that map platform protection capabilities
and deployment requirements to particular regulatory or organizational
regimes.

Examples may include:

- HIPAA
- organizational privacy policies
- contractual confidentiality requirements
- jurisdiction-specific privacy requirements
- future industry-specific protection regimes

A compliance profile may impose additional requirements without changing
fundamental EMF Evidence or Domain Extension semantics.

Support for a compliance profile does not by itself establish that a
particular EMF deployment or organization is legally compliant.

Compliance depends on the complete deployed system and applicable
administrative, technical, physical, contractual, and operational controls.

Deployment-specific concerns may include:

- identity-provider configuration
- authorization policy
- key management
- encryption configuration
- network security
- backup protection
- logging configuration
- retention policy
- incident response
- contractual agreements
- approved external service providers
- organizational procedures

## Consequences

Benefits:

- protected information is governed consistently across domains
- HIPAA-related requirements do not become embedded in Veterans Claims
- future domains can reuse the same protection architecture
- domain Evidence Classification remains semantically distinct
- EISL provider selection can enforce protection policy
- access and disclosure decisions become explicit
- security activity can be audited without redefining Evidence provenance

Tradeoffs:

- the platform requires new security and information-governance abstractions
- deployments must configure protection policy correctly
- EISL provider selection becomes policy-aware
- persistence and external-service implementations may require additional
  security capabilities
- compliance cannot be guaranteed solely by installing EMF

## Relationship to Existing Architecture

ADR-012 remains authoritative for the Domain Extension and platform boundary.

ADR-013 remains authoritative for Veterans Claims domain concepts and
veterans-domain Evidence Classification.

The Evidence Storage Model remains authoritative for Artifact identity,
provenance, relationships, fingerprints, and extensible metadata.

This decision adds a distinct platform information-protection concern and
does not redefine those existing concepts.


## Accepted Amendment — 2026-10-01: Authoritative Classification and Secured Disclosure/Export

**Status:** Accepted
**Date:** 2026-10-01

This dated amendment extends the accepted decision. Original text above is preserved as architectural history. Where explicitly clarified below, this accepted amendment governs the current architecture; unrelated original decisions remain in force.

### Cross-ADR identities and dependency direction

Core-facing contracts MUST use Core-owned provider-neutral identity/value representations for operation, receipt, ownership, classification reference/revision and audit-obligation identity where those values cross the Core boundary. EMF.Core MUST NOT reference EMF.Security or its concrete models. Security owns authorization, classification authority, canonical audit-event models and lifecycle policy, and maps those models to the neutral contract representations. AuditEventId, ClassificationRevision, OperationId, Receipt and OwnershipToken retain their distinct validated identity semantics across that mapping; shared spelling or representation does not make them interchangeable. SQLite catalog and Azure SDK types remain implementation details outside Core.

Physical Revision identifies a store-issued object generation; provisional OwnershipToken establishes lifecycle ownership; mutation OperationId identifies a logical mutation and Receipt records its durable outcome; ClassificationRevision identifies authoritative classification state; AuditEventId identifies one canonical audit event. These are separate typed concepts. None silently substitutes for another. Identifiers are bounded, validated, non-sensitive values and contain no plaintext, wrapped key bytes, credentials or DEK/KEK material. An ownership token is an opaque coordination identifier, not a bearer authorization credential; authorization is independently required.

No Azure SDK types enter EMF.Core or provider-neutral EMF.Security contracts. Physical stores remain cryptography-unaware. Provider failures follow ADR-020 typed sanitized model. Recovery is authorized under its service identity, preserves the original actor separately, and is audited through ADR-049. Deployment policy, classification governance, recovery schedules, historical-key retention and alert escalation remain deployment obligations.

### Amendment scope and decision

Extend Policy Enforcement, Intelligence Services Boundary and Auditability without changing domain/protection classification separation, resource-neutral ADR-031 semantics or deployment compliance obligations.

Provide a platform authoritative Artifact protection-classification resolver and shared secured disclosure/export executor in EMF.Security. Caller-supplied classification is a claim to verify, never a substitute for authoritative resolution. Unknown/missing/unresolvable classification fails closed. Resolver returns actual resource identity, classification, ClassificationRevision and a defined freshness/coordination capability. Classification assignment/governance is authorized and independently audited.

Internal read/render requires applicable access authorization but is not automatically disclosure. Controlled local export requires export permission and a registered destination. External transmission/publication requires disclosure permission and provider/destination eligibility. Encryption at rest does not authorize either.

### Required execution protocol

1. Resolve actual Artifact or output identity and exact output/content revision or immutable snapshot; validate contributing Artifact identities for derived output.
2. Resolve authoritative protection classification and ClassificationRevision.
3. Resolve a bounded registered destination identity; actual endpoints/paths are separately secured configuration. Identity never contains credentials, protected paths or arbitrary user content. Redirects/provider substitutions require an approved destination or reauthorization.
4. Authorize via existing resource-neutral policy with actual resource type/id, actor, operation, classification and destination. Bind decision to ClassificationRevision and exact output/input identities.
5. Immediately before the first disclosure/export byte, verify that the originally authorized ClassificationRevision is still authoritative. Changed revision requires re-evaluation or fail-closed denial.
6. Execute only through a trusted composition-registered disclosure handler resolved by the executor for the authorized operation kind, registered DestinationId and applicable output/resource type. No reusable naked allow boolean, arbitrary caller delegate/IDisclosureOperation, caller-controlled bypass or console-only duplicated policy.
7. Audit decision and outcome with identical resource, actor, classification, destination and revision identities. Include UTC time and bounded failure category; never output content.

A verify-then-send gap is not accepted as atomicity. Classification authority must support a short read lease/fence that serializes reclassification through disclosure initiation, or a destination-side acceptance mechanism with equivalent enforced preconditions. For long streams define the policy boundary explicitly: authorization applies to the immutable snapshot and initiation under the fence; classification changes after initiation cannot recall bytes already released. Deployments requiring midstream revocation must use bounded chunks/revalidation and explicit cancellation semantics. Pure eventual classification caches cannot satisfy this requirement.

### Derived outputs

Define an explicit versioned classification-derivation policy over contributing Artifacts, their ClassificationRevisions, relevant content revisions and output identity. Do not assume a highest-classification rule: classification domains may not be ordered. Unknown inputs, unsupported combinations or absent policy deny export. Revalidate contributing revisions and policy version immediately before disclosure; changed contribution requires re-derivation and authorization. Historical/prepared output reuse does not bypass the gate. Audit individual Artifact identities or bounded structured references to a governed contributing-resource manifest without copying protected content.

### Illustrative contracts and layers

```csharp
Task<ResolvedProtectionClassification> ResolveAsync(ArtifactId id, CancellationToken ct);
Task<DisclosureResult> ExecuteAsync(DisclosureRequest request, CancellationToken ct);
```

Ordinary callers supply only a validated request naming resources/output, operation kind and registered DestinationId. They MUST NOT supply executable code, an arbitrary handler instance, transport, endpoint override or delegate after authorization. The executor MUST resolve a trusted composition-registered capability whose structural registration binds operation kind, DestinationId and supported resource/output type; lookup ambiguity or binding mismatch fails closed. Registration is controlled by the trusted composition root, not runtime user input. Handler dependencies/transport configuration MUST enforce that destination binding; a mere string comparison before an unrestricted caller callback is insufficient. The executor passes a non-forgeable internal authorization context bound to the actual identities, output snapshot and ClassificationRevision. Handlers cannot substitute destination/resource/type; redirects or endpoint/provider changes require registered-destination validation and reauthorization before release of any further bytes. Internal rendering may return a snapshot, but may not inject arbitrary disclosure code.

The resolver MUST also support ADR-048's governed provisional classification authority for pre-adoption resource operations; it is not a normal caller-supplied classification shortcut. Provisional authority cannot authorize disclosure of canonical/adopted resources. Tests MUST reject arbitrary executable callbacks, forged capabilities, handler/resource mismatch, unregistered destinations and redirection outside the authorized registration.

Security owns policy resolver/executor contracts and context binding; persistence/provider adapters implement authoritative revision/fencing; composition registers destinations and injects the executor. Domain Extensions supply domain facts/rendering, not security policy or cryptography. Existing EISL capability/provider eligibility remains authoritative for intelligence processing and consumes verified classifications.

### Exact current migration boundaries

- src/EMF.Security/Models/ArtifactProtectionClassification.cs — association becomes resolvable authoritatively with revision.
- src/EMF.Security/Authorization/AuthorizationRequest.cs and Services/CompositeAuthorizationPolicy.cs — retain resource-neutral semantics; bind verified classification at execution.
- src/EMF.Intelligence/Execution/IntelligenceCapabilityExecutor.cs:94,110 — replace trusted caller classification with authoritative input resolution while preserving provider selection policy.
- src/EMF.Security/Storage/ArtifactEnvelopeRewrappingService.cs — resolve actual Artifact classification for mutation/recovery authorization.
- src/EMF.Console/VeteransConsoleCommand.cs:6741,6744,6786,6844,6856,6991 — prepared/historical reuse and actual DOCX/PDF export; wrap output operation before durable file release.
- src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerPackageDocumentOutputService.cs:94 — expose stable output/contributor context; internal rendering alone is not disclosure.
- src/EMF.Console/IntelligenceConsoleCommand.cs:166 and TextInsightConsoleOutputWriter.cs — summary/keyword terminal output.
- src/EMF.Console/TextSummarizationConsoleOutputWriter.cs:8 — summary output gate; live invocation remains subject to separate deployment authorization.
- src/EMF.Console/ConsoleAuthorizationPolicyFactory.cs and ArtifactContentStoreFactory.cs — platform composition, not scattered command policy.
- src/EMF.Console/VeteransReviewerPackagePublisher.cs:17,71 and TextInsightConsoleEvidencePublisher.cs:14 — assess internal evidence/package promotion for access and derived classification; do not classify method names as external disclosure without actual boundary crossing.

### Migration, verification and tradeoffs

Register destinations, configure authoritative classification persistence and derivation policy, classify legacy resources or deny export, then migrate every actual export boundary including reused output. No export path may keep caller-only classification. Preserve existing evidence/provenance and internal render behavior under access policy.

Test unknown classification, caller mismatch, reclassification between authorization and export, concurrent fencing, changed derived contributors, unordered classification combinations, destination substitution, internal rendering vs export, reused output and exact authorization/audit identity agreement. Destination/identity schemas are bounded allowlists. Streaming revocation and derivation policy content remain explicit deployment-approved choices, not hidden permissive defaults. References: ADR-012/017/026/031, ADR-047/049.

### Related architecture decisions

- [ADR-012](ADR-012-domain-extension-platform-boundary.md)
- [ADR-020](ADR-020-azure-key-management-adapter-boundary.md)
- [ADR-026](ADR-026-intelligence-services-agent-boundary.md)
- [ADR-031](ADR-031-resource-neutral-authorization.md)
- [ADR-047](ADR-047-versioned-artifact-content-mutation.md)
- [ADR-048](ADR-048-artifact-ingestion-ownership-and-recovery.md)
- [ADR-049](ADR-049-security-sensitive-mutation-audit-delivery.md)
