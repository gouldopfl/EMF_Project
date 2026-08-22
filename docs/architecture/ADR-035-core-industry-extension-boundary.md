# ADR-035: Core and Industry Extension Boundary

- Status: Accepted
- Date: 2026-08-22

## Context

EMF is intended to support multiple industries through industry-specific
extensions and adapters.

During initial platform development and shake-down, industry-specific
requirements must not be allowed to alter the universal EMF core in ways
that create dependencies on a particular industry.

Changes that are appropriate for one industry may not be appropriate for
other industries and could weaken the portability, reuse, or stability of
the core platform.

The core must therefore remain focused on capabilities that are
demonstrably industry-independent.

## Decision

EMF Core will remain industry-neutral.

Industry-specific behavior, rules, requirements, terminology, workflows,
evidence requirements, and domain policies MUST be implemented through
industry extensions or adapter boundaries unless the behavior can be
demonstrated to be industry-independent.

A proposed change to EMF Core MUST be evaluated using the following
question:

> Would this capability be required, or be valid, across all supported
> industries?

If the answer is no, the capability belongs outside the core.

Industry extensions MAY depend on EMF Core contracts and universal
services, but EMF Core MUST NOT acquire dependencies on individual
industry extensions.

Industry adapters MUST NOT require unrestricted modification of EMF Core
to implement industry-specific functionality when an existing extension
contract can support the requirement.

During the initial platform validation and shake-down period, control of
the EMF Core architecture remains with the project steward. External
industry adapter development may be introduced through defined extension
contracts without granting unrestricted authority over the core.

## Consequences

### Positive

- Protects EMF Core from industry-specific coupling.
- Allows multiple industries to evolve independently.
- Reduces the risk that one adapter breaks unrelated industries.
- Establishes a clear code-review criterion for proposed core changes.
- Allows outside contributors to develop adapters within controlled
  boundaries.
- Preserves the long-term portability and reuse of the EMF platform.

### Negative

- Some functionality may require additional extension contracts.
- Cross-boundary design requires more deliberate architectural decisions.
- Industry adapters may not be able to directly modify core behavior.

## Examples

Universal capabilities such as artifact identity, content storage,
integrity, provenance, lineage, security, audit, workflow infrastructure,
content extraction contracts, and intelligence contracts belong in the
core when they remain industry-independent.

Industry-specific requirements such as Veterans Affairs evidence rules,
CFR requirements, claim-specific evidence guidance, or other industry
regulatory policies belong in the applicable industry extension.

## Governance Rule

When an industry adapter requires a new capability, the preferred order
of evaluation is:

1. Use an existing core contract.
2. Use an existing extension contract.
3. Add or extend an industry-specific contract.
4. Only then consider modifying EMF Core.

Any proposed core modification must demonstrate why the capability is
industry-independent.
