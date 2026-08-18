# ADR-033: Workflow Operation Idempotency

**Status:** Accepted
**Date:** 2026-08-18

## Context

Workflow activities may perform external side effects. A failure after an
external request has been issued can leave the outcome unknown.

Creating a new operation identifier during recovery could therefore execute the
same logical side effect more than once.

## Decision

`OperationId` is the stable identity of a logical workflow operation.

Retries of the same logical operation preserve its `OperationId`. A retry is
another execution attempt, not a new logical operation.

Operation recovery follows these rules:

- completed operations are not automatically replayed
- pending operations require review
- operations with unknown external outcomes require review
- known-safe failed operations may be retried
- duplicate logical operations are rejected by the existing persistence boundary

EMF provides the logical operation identity and replay boundary. External
providers remain responsible for honoring provider-specific idempotency
requirements.

No separate attempt-history model is introduced by this ADR.

## Consequences

Benefits:

- retries retain logical operation identity
- duplicate operations can be detected
- completed and uncertain operations fail closed
- existing `OperationId` infrastructure is reused

Limitations:

- EMF cannot determine the outcome of arbitrary external side effects
- external providers may require their own idempotency mechanisms
- execution-attempt history is not separately persisted

## Verification

Tests will verify:

- retry preserves `OperationId`
- duplicate operations are rejected
- completed operations are not replayed
- pending and unknown outcomes require review
- known-safe failures may be retried

## References

- ADR-008: Workflow Failure Retry Semantics
- ADR-023: Workflow Resume and Restart Semantics
- ADR-024: Workflow Recovery Policy
- ADR-025: Workflow Recovery Coordinator Boundary
- ADR-029: Workflow Optimistic Concurrency
- ADR-030: Workflow Activity Claim Recovery
