# ADR-003: Workflow Execution Lifecycle

## Status

Accepted

## Context

EMF performs long-running operations such as:

- evidence discovery
- inventory creation
- fingerprint calculation
- persistence
- analysis

These operations may take significant time and may need recovery after interruption.

The platform requires a consistent way to track execution progress.

## Decision

EMF will manage long-running operations through a workflow lifecycle.

A workflow represents the execution state of an operation.

The workflow service is responsible for:

- starting workflows
- recording checkpoints
- marking completion
- recording failures
- enabling recovery

The workflow service does not:

- store evidence directly
- perform domain processing
- replace orchestration services

## Lifecycle

Example:

Started
  |
  v
Running
  |
  v
Checkpoint recorded
  |
  v
Completed


Failure path:

Running
  |
  v
Failed
  |
  v
Resume or restart

## Separation

Workflow:

- execution state
- progress
- recovery information

Evidence:

- artifacts
- relationships
- provenance
- fingerprints

## Consequences

Benefits:

- long-running operations become recoverable
- execution history is observable
- processing services remain focused
- workflows can evolve independently

Tradeoff:

The platform maintains explicit workflow state.

This complexity is intentional.
This complexity is intentional because recoverability,
observability, and long-running processing are core EMF capabilities.
