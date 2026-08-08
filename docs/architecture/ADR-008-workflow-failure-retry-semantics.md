# ADR-008: Workflow Failure and Retry Semantics

## Status

Accepted

## Context

EMF workflows execute ordered activities that may succeed or fail.

Activity results are persisted as workflow checkpoints, and workflow execution
can resume from previously completed checkpoints.

A deterministic failure model is required so the platform does not continue
processing after a failed activity unless that behavior is explicitly defined.

## Decision

A failed activity stops the current workflow execution.

When an activity returns a failed result:

1. The failed activity result is persisted as a workflow checkpoint.
2. No later activity is executed during that workflow run.
3. The failed activity is not considered completed.
4. A later execution may retry the failed activity.
5. Automatic retry is not performed by the workflow runner.

Only checkpoints with WorkflowStatus.Completed cause an activity to be skipped
during resume.

Retry policy will remain separate from the core workflow runner so future
domains may define appropriate retry behavior without changing basic execution
semantics.

## Execution Model

Execute Activity
        |
        v
Activity Result
        |
        +---- Success ----> Record Completed Checkpoint
        |                         |
        |                         v
        |                  Continue Workflow
        |
        +---- Failure ----> Record Failed Checkpoint
                                  |
                                  v
                           Stop Current Run

## Consequences

Benefits:

- failed work cannot silently allow dependent activities to continue
- recovery behavior remains deterministic
- failures remain visible in persisted execution history
- retry policy is explicit rather than hidden
- domains can later define different retry strategies

Tradeoff:

Transient failures require a later retry policy or another workflow execution.

This is intentional because automatic retry behavior can vary significantly
between activities and domains.
