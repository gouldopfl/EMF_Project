# ADR-013: Veterans Claims Domain Model Boundary

## Status

Proposed

## Context

ADR-012 established that domain-specific functionality belongs in Domain
Extensions and that the EMF platform remains domain-neutral.

The Veterans Claims Domain Extension is the first concrete Domain Extension.

The extension now requires a domain model that can represent veterans' claims
without duplicating or redefining EMF platform concepts such as evidence,
provenance, workflow execution, persistence, or integrity.

A veterans' claim may contain multiple separately adjudicated issues. Each
issue may involve a different claimed condition, service-connection theory,
evidence set, decision, disability evaluation, and effective date.

The domain model must therefore distinguish the overall claim submission from
the individual issues evaluated within that claim.

## Decision

The Veterans Claims Domain Extension will define veterans-specific concepts
within the extension boundary.

The initial domain model will distinguish at least the following concepts:

- Veteran
- Claim
- Claim Issue
- Claimed Condition
- Service Connection Theory
- VA Decision
- Disability Evaluation
- Effective Date

A Claim represents a veterans' benefits claim or submission that may contain
one or more Claim Issues.

A Claim Issue represents an independently adjudicable matter within a Claim.

A Claim Issue may identify:

- one or more Claimed Conditions
- one or more Service Connection Theories
- supporting or opposing evidence
- adjudicative findings
- a VA Decision
- a Disability Evaluation
- an Effective Date

Claim Issue is the primary veterans-domain unit for adjudication modeling.

The Veterans Claims Domain Extension shall not create a separate evidence,
provenance, persistence, integrity, or workflow infrastructure.

Evidence used by the Veterans Claims domain remains represented through EMF
platform evidence and Artifact concepts.

The Veterans Claims Domain Extension may classify, interpret, associate, and
evaluate platform evidence using veterans-specific terminology and rules.

Domain objects may reference platform identities or contracts where needed,
but platform projects shall not depend on Veterans Claims domain types.

## Service Connection

Service Connection Theory represents the asserted or evaluated relationship
between a claimed condition and military service or another service-connected
condition.

The domain model may support theories including:

- direct service connection
- secondary service connection
- aggravation
- presumptive service connection
- other theories defined by veterans-benefits law or policy

The precise rule set for evaluating these theories is not defined by this ADR.

## Decisions and Ratings

A VA Decision represents an adjudicative outcome associated with one or more
Claim Issues.

A Disability Evaluation represents the percentage or other evaluation assigned
to a service-connected condition or issue.

An Effective Date represents the date from which an awarded benefit or
evaluation becomes effective.

Decision logic, rating schedules, combined-rating calculations, and effective
date rules are domain Policies and are not fundamental EMF platform behavior.

## Platform Boundary

The intended separation is:

```text
EMF Platform
    |
    +---- Evidence / Artifacts
    +---- Provenance
    +---- Relationships
    +---- Workflow
    +---- Persistence
    +---- Integrity
    |
    v
Veterans Claims Domain Extension
    |
    +---- Veteran
    +---- Claim
    +---- Claim Issue
    +---- Claimed Condition
    +---- Service Connection Theory
    +---- VA Decision
    +---- Disability Evaluation
    +---- Effective Date
```

The Veterans Claims Domain Extension interprets and organizes platform evidence
for veterans-benefits purposes but does not replace platform semantics.

## Consequences

Benefits:

- multiple issues within one claim can be modeled independently
- different service-connection theories can be associated with individual
  issues
- ratings and effective dates can be represented at the appropriate
  adjudication level
- platform evidence remains reusable across domains
- veterans-specific terminology remains outside EMF.Core
- future veterans workflows can operate on stable domain concepts
- domain Policies can evolve independently from platform infrastructure

Tradeoffs:

- Claim and Claim Issue must remain distinct concepts
- domain objects must reference platform evidence rather than own a separate
  evidence subsystem
- some VA processes may require additional domain concepts as the model
  evolves
- detailed adjudication and rating rules require separate Policies and
  architectural decisions

## Architectural Principle

EMF owns evidence and process semantics.

The Veterans Claims Domain Extension owns veterans-specific interpretation,
adjudication concepts, and domain rules.
