# ADR-029: Workflow Optimistic Concurrency

**Status:** Accepted
**Date:** 2026-08-16

## Context

Workflow execution state controls whether activities run, resume, complete, or
fail. Concurrent operators, workers, or recovery attempts could read the same
state and then overwrite one another. An older write could conceal a newer
decision or append a transition that no longer matches the authoritative
execution state.

The SQLite workflow repository previously updated rows by workflow identifier
alone. It could not distinguish a current mutation from one based on stale
state.

## Decision

Each `WorkflowExecutionRecord` carries a nonnegative revision value.

The SQLite `Workflows` table includes a `Revision` column. Existing databases
receive the column through the additive schema upgrade with a default value of
zero. Newly created executions also begin at revision zero.

Execution updates use a compare-and-increment operation:

- the update matches both workflow identifier and expected revision
- a successful update increments the stored revision atomically
- zero affected rows fail with `WorkflowConcurrencyException`
- callers must reload authoritative state before making another decision

Status changes and transition-history insertion occur in one transaction.
A stale status update therefore rolls back without appending a misleading
transition record.

Workflow services preserve the loaded revision when constructing updated
execution records. The repository does not automatically retry stale writes,
because retrying a workflow decision without reevaluation could apply an
obsolete decision to newer state.

## Consequences

Benefits:

- stale execution updates fail closed
- concurrent status changes cannot silently overwrite each other
- stale transitions cannot be appended after a rejected state update
- legacy databases upgrade without rewriting existing workflow records
- concurrency failures are distinguishable from general persistence failures

Costs and limitations:

- callers must propagate the revision from the state they evaluated
- a rejected caller must reload and reevaluate before retrying
- revision checks do not authenticate the caller
- direct database modification remains possible for an actor with write access
- checkpoints and transition history are not yet independently hash-chained
- valid state captured with its current revision can still be replayed unless
  higher-level operation identifiers or idempotency controls reject it

## Verification

Automated tests verify:

- new and upgraded workflow schemas expose revisions
- successful status transitions increment revisions
- stale standalone updates throw `WorkflowConcurrencyException`
- stale transactional transitions do not alter execution state
- rejected stale transitions do not append history

## Follow-up Work

- authenticate and authorize workflow mutations
- add operation identifiers and replay or duplicate detection
- evaluate integrity protection for checkpoints and transition history
- audit administrative workflow changes
- exercise concurrent recovery behavior
- protect and test workflow backups

## References

- `docs/THREAT_MODEL.md`, TM-07
- ADR-023: Workflow Resume and Restart Semantics
- ADR-024: Workflow Recovery Policy
- ADR-025: Workflow Recovery Coordinator Boundary
