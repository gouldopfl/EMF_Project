# ADR-030: Workflow Activity Claim Recovery

**Status:** Accepted  
**Date:** 2026-08-17

## Context

A durable activity claim prevents concurrent workers from executing the same
workflow activity. If the owning process fails before completing or releasing
its claim, the activity can remain blocked indefinitely.

Automatically stealing an old claim is unsafe because age alone does not prove
that the original activity stopped or that its external effects are repeatable.

## Decision

Activity claims remain exclusive by workflow and activity identifier.
`ClaimId` is the ownership and fencing token.

Abandoned-claim recovery is an explicit operation. The caller supplies:

- the workflow and activity identifiers
- a new claim identifier
- the recovery time
- an `abandonedBeforeUtc` cutoff

SQLite atomically transfers ownership only when the existing claim:

- remains in `Claimed` status
- was claimed at or before the supplied cutoff

Fresh claims and completed claims cannot be reclaimed. After transfer, the
former owner cannot complete or release the claim because its claim identifier
no longer matches.

Normal workflow execution does not automatically reclaim claims.

## Consequences

Recovery can unblock work after a confirmed worker failure without permitting
two current owners. Operators must consider external side effects before
restarting an activity.

Time-based abandonment is an administrative judgment, not proof that replay is
safe.

## Verification

Automated tests verify:

- abandoned claims transfer ownership
- former owners are fenced out
- fresh claims cannot be reclaimed
- completed claims cannot be reclaimed
- concurrent initial claims produce exactly one winner

## Follow-up Work

- authorize and audit claim-recovery operations
- define operational abandonment thresholds
- consider claim heartbeats or lease renewal
- require idempotency controls for external side effects
