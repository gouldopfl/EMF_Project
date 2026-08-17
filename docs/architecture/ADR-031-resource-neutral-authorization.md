# ADR-031: Resource-Neutral Authorization Requests

**Status:** Accepted  
**Date:** 2026-08-17

## Context

`AuthorizationRequest` requires an `ArtifactId`, although subject-permission
authorization is resource-neutral. This prevents workflow administration from
using the established authorization pipeline without falsely representing a
workflow as an artifact.

## Decision

Authorization requests identify their target with:

- `ResourceType`
- `ResourceId`
- `ProtectionClassificationId`

Artifact callers use resource type `Artifact` and the artifact identifier
value. Workflow callers use resource type `Workflow` and the workflow
identifier value.

Typed domain identifiers remain at service boundaries and are converted to the
resource-neutral authorization identity when constructing a request.

Permission and protection policies remain fail-closed. Audit records use the
same resource type and identifier evaluated by authorization.

## Consequences

The authorization pipeline can protect artifacts, workflows, and future
resource types without parallel policy systems or false artifact identities.

Callers and tests must migrate from the artifact-specific request property.
Resource-specific services remain responsible for validating their typed
identifiers before authorization.

## Verification

Automated tests must verify:

- artifact authorization preserves artifact resource identity
- workflow requests preserve workflow resource identity
- permission denial remains fail-closed
- protection classification remains enforced

## Follow-up Work

- authorize and audit abandoned workflow-claim recovery
- define constants for supported authorization resource types
- evaluate stronger typed resource identities if string misuse emerges
