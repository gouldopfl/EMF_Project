# ADR-011: Presentation and Deployment Independence

## Status

Accepted

## Context

EMF is intended to support multiple users and organizational environments without coupling its evidence-processing framework to a particular user interface or deployment model.

Individual users may operate EMF locally or through a hosted service. Organizations such as Veterans Service Organizations, government agencies, or other authorized service providers may prefer centralized browser-based access integrated with their existing infrastructure, identity systems, security policies, and operational controls.

The core EMF framework must therefore remain independent of how users access the system.

## Decision

EMF core processing, evidence, workflow, provenance, and domain services shall remain independent of the presentation and deployment model.

Presentation layers are consumers of EMF services and shall not contain or control core evidence-processing logic.

EMF may therefore be exposed through one or more presentation models, including:

- Browser-based applications
- Desktop applications
- APIs
- Organization-hosted environments
- Cloud-hosted environments
- Private or hybrid deployments

A browser-based deployment may allow an organization to integrate EMF into its existing web infrastructure and identity environment while maintaining centralized administration and updates.

No presentation model shall require changes to the fundamental EMF evidence or workflow architecture.

## Consequences

This decision:

- Preserves UI independence in the EMF core.
- Allows browser-based organizational deployment without redesigning the evidence framework.
- Supports centralized updates and administration.
- Allows organizations to apply their own authentication, authorization, security, and hosting policies.
- Preserves the possibility of local or desktop operation where appropriate.
- Encourages stable service contracts between EMF core capabilities and presentation layers.
- Prevents presentation-specific concerns from becoming dependencies of the core framework.

## Architectural Principle

EMF is browser-capable by design, but not browser-dependent.

The evidence framework remains the system of record and execution authority. Presentation layers provide controlled access to that framework without defining its behavior.
