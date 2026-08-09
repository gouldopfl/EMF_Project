# ADR-012: Domain Extension and Platform Boundary

## Status

Accepted

## Context

EMF is intended to support multiple industries and missions while preserving
a stable, reusable evidence and workflow platform.

The platform currently provides capabilities including evidence handling,
workflow execution, recovery, persistence, integrity, discovery, inventory,
and orchestration without depending on a particular domain.

Domain-specific expertise must now be introduced without embedding that
expertise into the EMF core.

Veterans' claims will be the first domain implementation, but the architecture
must also support future domains such as legal, healthcare, insurance,
compliance, engineering, human resources, and research.

EMF already defines an Extension as a component that adds capability without
requiring changes to the EMF core.

## Decision

Domain-specific functionality will be implemented as Domain Extensions.

A Domain Extension is an EMF Extension that contributes expertise,
configuration, workflows, Activities, Policies, or other capabilities specific
to a domain.

Domain Extensions may depend on stable EMF platform contracts.

The EMF platform shall not depend on a specific Domain Extension.

The platform retains authority over fundamental platform behavior including:

- evidence identity and provenance
- workflow execution and lifecycle
- checkpoint and recovery semantics
- persistence contracts
- integrity mechanisms
- orchestration boundaries
- platform security and policy enforcement

A Domain Extension may define or contribute:

- domain-specific workflow definitions
- domain-specific Activities
- domain terminology and classifications
- domain rules and Policies
- domain-specific evidence interpretation
- domain-specific capability composition

A Domain Extension shall not redefine fundamental platform execution,
evidence, persistence, provenance, or integrity semantics.

Presentation and deployment layers may compose the EMF platform with one or
more Domain Extensions without changing the underlying platform architecture.

EISL remains an independent provider-neutral intelligence boundary. Domain
Extensions may request intelligence capabilities through EISL but shall not
require the platform to depend on a particular AI provider.

## Dependency Direction

The intended dependency direction is:

    Presentation / Composition
              |
              v
       Domain Extensions
              |
              v
       Platform Contracts
              |
              v
    Platform Implementations

Platform implementations must not depend upward on a specific Domain Extension.

## Consequences

Benefits:

- veterans' claims can evolve without becoming part of the EMF core
- additional industries can reuse the same platform
- domain expertise remains independently replaceable and testable
- platform infrastructure remains domain-neutral
- presentation layers may select appropriate Domain Extensions
- intelligence providers remain independent from domain architecture
- extension developers can work against stable platform contracts

Tradeoffs:

- explicit extension boundaries and contracts must be maintained
- domain functionality cannot rely on undocumented platform internals
- composition must determine which Domain Extensions are available for a
  particular deployment

These constraints are intentional because domain independence is necessary for
EMF to remain a reusable evidence-centric platform.

## Architectural Principle

The platform owns the process; Domain Extensions provide the expertise; EISL
provides provider-independent intelligence capabilities.
