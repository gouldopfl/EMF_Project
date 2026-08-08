# ADR-005: Workflow Runner Execution Coordination

## Status

Accepted

## Context

EMF workflows consist of multiple activities that must execute in a controlled sequence.

Examples include:

- evidence discovery
- inventory processing
- artifact creation
- persistence
- integrity validation

The workflow lifecycle and the activities themselves must remain independent.

A component is needed to coordinate activity execution without owning domain processing logic.

## Decision

EMF will use a Workflow Runner to coordinate activity execution.

The Workflow Runner is responsible for:

- executing activities in order
- passing execution context
- coordinating activity completion
- handling execution failures

The Workflow Runner does not:

- implement domain processing
- replace workflow lifecycle management
- store evidence
- own persistence details

## Execution Model

Workflow

    |
    v

Workflow Runner

    |
    +--> Activity 1
    |
    +--> Checkpoint
    |
    +--> Activity 2
    |
    +--> Checkpoint
    |
    +--> Complete

## Separation

WorkflowService:

- manages workflow state
- records lifecycle transitions

WorkflowRunner:

- coordinates execution

Activities:

- perform individual work units

## Consequences

Benefits:

- execution logic remains modular
- activities can be reused
- failures can be isolated
- future parallel execution is possible

Tradeoff:

The platform maintains an additional coordination layer.

This complexity is intentional because EMF is designed for modular, recoverable processing.
