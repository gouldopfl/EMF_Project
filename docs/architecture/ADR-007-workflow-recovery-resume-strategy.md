# ADR-007: Workflow Recovery and Resume Strategy

## Status

Accepted

## Context

EMF performs long-running operations that may be interrupted by:

- process termination
- VM shutdown
- infrastructure interruption
- application restart

A workflow must distinguish between failure and interruption.

A failed workflow completed execution of an activity and received a failure result.

An interrupted workflow stopped before execution completed.

## Decision

EMF will support workflow recovery through explicit execution states and checkpoints.

Workflow recovery will use persisted execution history rather than in-memory state.

## Workflow States

Created

Running

Completed

Failed

Interrupted

Recoverable

## Recovery Rules

Completed activities are not executed again.

Interrupted workflows may resume from the last completed checkpoint.

Failed activities require explicit workflow handling before restart.

## Recovery Metadata

Workflow recovery may require:

- last successful checkpoint
- current activity identifier
- execution start time
- interruption detection time
- retry information

## Separation

Workflow execution state:

- lifecycle status
- checkpoints
- recovery metadata

Activity processing:

- domain work
- evidence handling
- analysis

## Consequences

Benefits:

- long-running operations survive interruption
- recovery behavior is predictable
- execution history remains observable

Tradeoff:

The platform maintains additional execution state.
