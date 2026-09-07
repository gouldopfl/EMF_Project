# ADR-039: AI Execution Traceability

## Status

Accepted

## Date

2026-09-07

## Context

ADR-026 requires provider execution metadata, provenance, audit, and review requirements for intelligence operations.

ADR-038 separates intelligence capability from operational authority.

As AI-assisted work becomes more capable and more autonomous, EMF must be able to reconstruct how consequential intelligence output was produced and under what authority it was promoted or acted upon.

Traceability must therefore cover the full execution path rather than only the final generated text.

## Decision

Consequential AI-assisted work shall preserve sufficient execution metadata to reconstruct the intelligence operation without requiring retention of plaintext protected prompts or responses.

Traceability shall identify, where applicable:

- capability requested
- acting identity or execution context
- input Artifact or Evidence identities
- provider identifier
- deployment, model, or engine identity and version when available
- agent identity when an agent coordinated the operation
- correlation and operation identifiers
- execution start and completion times
- configured protection classification
- tool or capability authorities available to the operation
- validation, warning, and review outcomes
- promotion or publication event
- approving human identity and role when required

## Evidence Lineage

AI-generated or AI-derived output is derived work product until explicitly promoted.

When promoted into Evidence or an authoritative domain record, EMF shall preserve lineage linking the promoted result to the originating intelligence execution and its source Artifacts or Evidence.

Promotion shall not erase the distinction between source evidence and derived intelligence.

## Configuration and Policy Context

Traceability shall preserve normalized identifiers for the provider, deployment, capability, and governing execution context needed to understand the result.

Provider-specific request and response objects shall remain inside provider adapters.

Complete prompts, complete responses, credentials, secrets, and plaintext protected content shall not be placed in audit metadata unless a separately authorized retention policy explicitly requires them.

## Validation and Review

Execution records shall preserve whether required validation succeeded, failed, or produced warnings.

Where policy requires human review or approval, the review state and approving identity shall be traceable to the consequential result.

A result shall not be represented as fully authorized or promoted when required validation, provenance capture, audit recording, or approval has failed.

## Tool and Agent Traceability

When an agent or capability can invoke tools or downstream operations, EMF shall preserve enough metadata to identify the consequential tools or authorities made available and the operations that materially affected the result.

Traceability shall distinguish recommendation from execution.

An AI recommendation to perform an action and the later authorized execution of that action are separate auditable events.

## Provider and Model Changes

Provider, deployment, model, engine, or agent changes shall remain observable in execution metadata.

A model upgrade shall not make earlier and later intelligence output indistinguishable when the relevant execution identity can be captured.

## Verification Requirements

Automated tests shall verify, where applicable, that:

- intelligence executions preserve required normalized metadata
- promoted intelligence preserves source and execution lineage
- provider and model identity remain traceable when available
- required human review is associated with the promoted result
- validation failure prevents successful promotion
- recommendation and consequential execution remain distinguishable
- audit metadata excludes prohibited secrets and plaintext protected content
- provider changes do not break provider-neutral consumer contracts

## Consequences

Benefits include reproducible intelligence lineage, stronger review and audit, clearer accountability, safer provider and model upgrades, and improved investigation of erroneous or compromised AI-assisted work.

Costs include additional metadata, persistence, correlation, and verification requirements.

## Rejected Alternatives

### Store only the final AI output

Rejected because the final text does not identify its source evidence, execution context, provider, validation, or authority.

### Store complete prompts and responses in audit records

Rejected because this can unnecessarily duplicate protected information and secrets into security-sensitive metadata stores.

### Treat model identity as irrelevant implementation detail

Rejected because provider neutrality does not eliminate the need to reconstruct which intelligence engine produced consequential derived work.

### Merge recommendation and execution into one event

Rejected because advice and authorized action have different authority and audit semantics.

## References

- ADR-017: Protected and Regulated Information Boundary
- ADR-026: Intelligence Services and Agent Boundary
- ADR-027: Initial Production Intelligence Provider
- ADR-028: Tamper-Evident Security Audit Storage
- ADR-038: AI Authority Separation

## Architectural Principle

Consequential AI-assisted work must be reconstructable.

EMF shall preserve enough provenance, execution identity, validation state, and authority context to explain how intelligence became an authorized result without weakening protected-information controls.
