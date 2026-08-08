# ADR-009: Stable Workflow Activity Identity

## Status

Accepted

## Context

EMF currently identifies workflow activities by their human-readable Name.

Workflow resume uses persisted checkpoints to determine whether an activity has
already completed.

Human-readable names may change over time for clarity, terminology, or domain
requirements.

Using a display name as persistent identity can therefore cause previously
completed work to be executed again after a rename.

## Decision

Workflow activities will have a stable machine-readable identifier separate
from their human-readable name.

Each workflow activity will expose:

- Id: stable activity identity used for persistence and recovery
- Name: human-readable display label

Checkpoint matching for resume behavior will use the stable activity Id.

Changing an activity Name must not change its persisted identity.

Activity IDs must be unique within a workflow definition.

## Identity Model

Workflow Activity
        |
        +---- Id ------> Persistence / Resume Matching
        |
        +---- Name ----> Human Display / Diagnostics

## Consequences

Benefits:

- activity renaming does not break workflow recovery
- persisted execution state remains stable across display changes
- machine identity and presentation concerns remain separate
- workflow definitions can evolve safely

Tradeoff:

Workflow authors must assign and preserve stable activity identifiers.

This requirement is intentional because durable workflow recovery depends on
stable identity across executions.
