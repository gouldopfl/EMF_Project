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
