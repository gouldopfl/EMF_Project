# ADR-007: Workflow Resume and Restart Semantics

## Status

Accepted

## Context

EMF workflows may perform long-running work across local, cloud, or distributed
execution environments.

Execution can be interrupted by:

- process termination
- machine restart
- network interruption
- service failure
- user-requested pause
- infrastructure shutdown

EMF already records workflow activity results as durable workflow checkpoints.

A deterministic recovery model is required so execution can continue without
repeating successfully completed work unnecessarily.

## Decision

Workflow recovery will be based on persisted workflow checkpoints.

The workflow repository is the authoritative source of completed execution
state.

When a workflow resumes:

1. Existing checkpoints are loaded for the workflow.
2. Activities with successful completed checkpoints are treated as completed.
3. Completed activities are skipped.
4. Execution resumes at the first activity without a successful completed
   checkpoint.
5. New activity results continue to create checkpoints normally.

A failed checkpoint does not mark an activity as successfully completed.

The activity may therefore become the recovery point for a later execution.

The runner must not rely on previous in-memory execution state when determining
where to resume.

## Execution Model

Workflow Definition
        |
        v
Load Checkpoints
        |
        v
Determine Completed Activities
        |
        v
Skip Completed Activities
        |
        v
Resume First Incomplete Activity
        |
        v
Continue Workflow Execution

## Consequences

Benefits:

- workflows survive process and machine interruption
- completed work is not unnecessarily repeated
- recovery behavior is deterministic
- persisted state remains authoritative
- long-running workflows can continue across execution environments

Tradeoff:

Workflow activity identity must remain stable enough to match activities with
their persisted checkpoints.

This complexity is intentional because EMF supports durable, recoverable,
long-running processing.
