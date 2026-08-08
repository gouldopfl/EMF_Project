# ADR-006: Activity Results and Workflow Checkpoint Integration

## Status

Accepted

## Context

EMF workflows execute long-running activities that may be interrupted.

The platform requires the ability to determine:

- what work completed
- what work failed
- where execution should resume

Activities currently execute independently, but workflow execution requires durable progress information.

## Decision

Workflow activities will produce execution results.

The Workflow Runner will use activity results to create workflow checkpoints.

Activities will not directly manage workflow persistence.

## Responsibilities

Activity:

- performs one unit of work
- returns execution information
- reports success or failure

Workflow Runner:

- executes activities
- interprets activity results
- creates checkpoints

Workflow Repository:

- stores execution history

## Execution Flow

Activity

    |
    v

Activity Result

    |
    v

Workflow Checkpoint

    |
    v

Workflow Repository

## Consequences

Benefits:

- workflows can resume after interruption
- execution history is available
- activities remain independent
- recovery logic stays centralized

Tradeoff:

The platform maintains explicit execution metadata.

This complexity is intentional because EMF supports long-running recoverable processing.
