# ADR-024: Workflow Recovery Decision Policy

## Status

Accepted

## Context

ADR-023 established persisted workflow recovery state.

EMF can now identify workflows that were interrupted through:

- persisted execution status
- workflow checkpoints
- recovery metadata

The platform must determine what action should be taken when an interrupted workflow is discovered.

Recovery decisions must remain separate from:

- workflow execution
- activity processing
- domain logic

## Decision

EMF will introduce a recovery decision policy responsible for evaluating incomplete workflows and determining the appropriate recovery action.

The recovery policy will evaluate:

- workflow execution state
- last successful checkpoint
- current activity identifier
- retry history
- interruption information
- activity recovery capability

## Recovery Decisions

A workflow recovery evaluation may produce:

- Resume
- Retry Activity
- Require Review
- Failed
- Abandoned

## Separation

Workflow Recovery Policy:

- evaluates state
- determines recovery action
- records recovery decision

Workflow Runner:

- executes activities
- records checkpoints
- manages execution order

Activities:

- perform domain work
- do not manage workflow recovery

## Consequences

Benefits:

- recovery behavior becomes explicit
- workflow execution remains deterministic
- recovery rules can evolve independently
- long-running workflows can survive interruption

Tradeoff:

The platform introduces an additional decision layer that must be tested and maintained.
