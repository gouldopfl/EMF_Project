# ADR-034: Evidence Requirement Guidance Boundary

**Status:** Accepted
**Date:** 2026-08-19

## Context

The Veterans Claims Domain Extension models Regulatory Authorities,
Regulatory Provisions, and Requirements independently from individual claims.

A Requirement identifies what must be established, considered, or applied.
It does not by itself describe which types of evidence may help establish
that Requirement.

Evidence guidance must remain distinct from Evidence itself. Guidance describes
potentially relevant evidence classifications and their relationship to a
Requirement without asserting that particular evidence exists, is sufficient,
or is legally required.

## Decision

The Veterans Claims Domain Extension will model evidence guidance as
`EvidenceRequirementGuidance`.

Each guidance entry is associated with a specific `Requirement` and identifies:

- an evidence classification
- a guidance role
- a description explaining how that evidence may relate to the Requirement

Guidance roles may include:

- SupportsRequirement
- EstablishesElement
- Corroborates
- Clarifies

Evidence guidance is reusable regulatory-domain knowledge. It is not attached
directly to an individual Claim Issue.

`RegulatoryEvidenceGuidanceService` composes Regulatory Provisions,
Requirements, and their associated Evidence Requirement Guidance for consumers.

Requirements remain visible when no guidance has been modeled.

Regulatory Provisions remain valid when no Requirements have been modeled.

Guidance for one Requirement must not be returned for another Requirement.

## Boundary

Evidence Requirement Guidance:

- describes potentially relevant evidence
- does not represent Evidence
- does not assert that Evidence exists
- does not determine evidentiary sufficiency
- does not determine credibility or weight
- does not establish a legal or medical conclusion
- does not automatically mean that the identified evidence is legally required

AI-assisted evidence-development summaries:

- may translate and organize supplied Evidence Requirement Guidance for
  veteran-oriented presentation
- may identify document or record types only when supported by the supplied
  guidance
- may explain the relevance of an identified evidence type when supported by
  the supplied guidance
- must distinguish required evidence from supporting evidence only when that
  distinction is present in the supplied guidance
- must not invent evidence requirements, document types, medical opinions,
  forms, or other facts
- are evidence-development guidance and not adjudication decisions

Platform Evidence and Artifact identity, provenance, integrity, and storage
remain owned by EMF platform infrastructure.

## Consequences

Benefits:

- regulatory Requirements can identify useful evidence classifications
- veterans and authorized reviewers can understand what evidence may support a Requirement
- guidance remains reusable across Claim Issues
- regulatory knowledge remains separate from individual claim evidence
- missing guidance does not hide valid Requirements
- missing Requirements do not invalidate Regulatory Provisions
- future evidence-gap and development-plan analysis can consume structured guidance

Limitations:

- guidance requires curated regulatory-domain knowledge
- guidance does not determine whether available Evidence satisfies a Requirement
- regulatory changes may require guidance updates
- claim-specific evidence analysis remains a separate responsibility

## Verification

Tests verify:

- evidence guidance persists and round-trips correctly
- guidance can be queried by Requirement
- guidance remains scoped to its Requirement
- Requirements without guidance remain visible
- Regulatory Provisions without modeled Requirements remain valid
- regulatory requirements and evidence guidance can be composed through the service boundary
- AI evidence-development summaries remain grounded in supplied guidance
- AI summaries do not introduce unsupported evidence requirements
- AI summaries preserve the distinction between evidence-development guidance
  and adjudication

## References

- ADR-012: Domain Extensions
- ADR-013: Veterans Claims Domain Model Boundary
