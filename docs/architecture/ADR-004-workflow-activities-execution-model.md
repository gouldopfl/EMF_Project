# ADR-004: Workflow Activities and Execution Model

## Status

Accepted

## Context

EMF performs complex, long-running processing operations.

Examples include:

- evidence discovery
- inventory creation
- artifact generation
- persistence
- integrity verification

A workflow must coordinate these operations while remaining independent of specific domain implementations.

A centralized workflow controller that directly invokes every service would create excessive coupling.

## Decision

EMF workflows will be composed of ordered activities.

Each activity represents one unit of responsibility.

The workflow engine coordinates activity execution but does not implement domain processing logic.

Activities are independently defined and executed.

## Activity Responsibilities

An activity is responsible for:

- performing one processing step
- reporting success or failure
- producing execution information
- supporting checkpoint creation

An activity does not:

- manage workflow lifecycle
- store workflow state directly
- coordinate unrelated activities

## Execution Model

Example:

Workflow

    |
    v

Activity 1
    |
    v
Checkpoint

Activity 2
    |
    v
Checkpoint

Activity 3
    |
    v
Complete

## Consequences

Benefits:

- workflow execution remains modular
- new capabilities can be added without changing the engine
- activities can be reused across workflows
- failures can be isolated and recovered

Tradeoff:

The platform requires explicit activity definitions and execution metadata.

This complexity is intentional because EMF is designed for long-running, recoverable processing.
