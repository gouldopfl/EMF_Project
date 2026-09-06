# ADR-001: Domain Independence

## Status

Accepted

## Context

The Evidence Management Framework is intended to serve multiple industries,
institutions, evidence domains, and regulatory environments.

The framework therefore requires a foundational boundary between concepts that
are universally reusable and concepts that depend on specialized domain
knowledge.

Without that boundary, the first implemented domain could introduce its own
terminology, rules, regulatory assumptions, or processing semantics into the
EMF core and make the platform increasingly difficult to reuse.

This decision was originally recorded as ADR-0001 in `docs/DECISIONS.md`.
This architecture record restores that foundational decision into the numbered
architecture ADR sequence.

## Decision

The Evidence Management Framework core shall remain independent of any single
industry, institution, data source, regulation, or evidence domain.

Domain-specific terminology, rules, interpretation, and processing shall be
implemented in domain models and adapters.

## Rationale

EMF is intended to serve many industries.

Coupling the core to its first implementation would reduce reuse and make
future maintenance harder.

## Consequences

- Core documentation and code use domain-neutral terminology.
- Domain-specific behavior remains outside the EMF core.
- Domain models may evolve independently of the core.
- New capabilities must be evaluated to determine whether they are universally
  reusable or require specialized domain knowledge.
- Later architectural decisions may refine domain boundaries without weakening
  this principle.

The original decision referred to the first domain implementation as the
"Veteran Model." Later architecture decisions formalized these specialized
models as Domain Extensions, including the Veterans Claims Domain Extension.

## Relationship to Later Decisions

ADR-012 formalizes the Domain Extension and platform boundary.

ADR-013 applies that boundary to the Veterans Claims Domain Extension.

ADR-035 further defines the boundary between the EMF core and industry
extensions.

These decisions refine and apply ADR-001; they do not supersede it.

## Architectural Principle

The EMF core owns universally reusable evidence and processing abstractions.

Specialized terminology, interpretation, regulation, and domain rules belong
outside the core.
