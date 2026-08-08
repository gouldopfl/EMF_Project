# ADR-010: Workflow Definition and Versioning

## Status

Accepted

## Context

EMF currently models workflow execution, activities, checkpoints, recovery,
failure handling, and stable activity identity.

The platform still needs a durable concept describing what a workflow is
independently from any particular execution of that workflow.

Workflow definitions may evolve over time.

An execution that started under one definition must not silently resume using
a different definition after activities, ordering, or behavior have changed.

## Decision

EMF will separate workflow definition identity from workflow execution identity.

A workflow definition will contain:

- Id: stable identity of the workflow definition
- Name: human-readable name
- Version: immutable version identifier
- Activities: ordered activity definitions for that version

Each workflow execution will be associated with the exact workflow definition
Id and Version under which it started.

A workflow definition version is immutable once used for execution.

Changes to activity ordering, identity, or execution meaning require a new
workflow definition version.

An interrupted execution must resume using the same definition version that
created its persisted execution state.

## Model

Workflow Definition
        |
        +---- Definition Id
        |
        +---- Version
        |
        +---- Ordered Activities
                |
                v
        Workflow Execution
                |
                v
             Results
                |
                v
           Checkpoints

## Consequences

Benefits:

- workflow structure is separated from runtime execution state
- paused workflows cannot silently change behavior during recovery
- historical executions remain reproducible
- workflow evolution becomes explicit
- activity ordering belongs to the definition rather than ad hoc runner input

Tradeoff:

The platform must retain or otherwise resolve older workflow definition
versions while executions still depend on them.

This complexity is intentional because durable workflow recovery requires the
execution model that created persisted state to remain identifiable.
