# ADR-043: Long-Running Operation Progress Reporting

## Status

Accepted

## Date

2026-09-08

## Context

EMF performs operations that may take substantial time, including evidence
processing, intelligence analysis, package preparation, rendering, and export.

A long-running operation that produces no visible activity may appear stalled
or crashed even when it is operating correctly.

A motionless screen can cause users to terminate valid operations prematurely.

Progress information must remain truthful. Some workflows have a known total
amount of work while others discover additional work dynamically.

This requirement is industry-independent.

## Decision

EMF shall provide visible liveness and meaningful progress information for
user-facing operations that may run long enough to appear unresponsive.

A long-running user operation shall not present a motionless interface that is
indistinguishable from a stalled or crashed application.

EMF shall not invent completion percentages when the total amount of work is
unknown.

## Progress Information

Progress may include:

- current operation stage
- completed work units
- total work units when genuinely known
- most recent activity timestamp
- completion, cancellation, or failure state

When total work is unknown, EMF shall use indeterminate progress rather than a
fabricated percentage.

Examples include:

- `Analyzing evidence`
- `103 analysis operations completed`
- `Still working`
- `Last activity: 20:37:39`

Technical implementation details such as cloud-provider calls, model names,
internal artifact IDs, and infrastructure operations shall not appear in
ordinary user-facing progress.

## Liveness Requirements

Graphical interfaces should provide continuous visible activity while work is
running.

Command-line interfaces should provide periodic status during long-running
operations.

As a default, user-facing status should refresh at least every 30 seconds when
no more meaningful progress event has occurred.

## Architectural Boundary

Core shall define the progress-reporting abstraction and progress-state model.

Universal Orchestration services shall emit progress as work is performed.

Domain extensions may translate progress into domain-appropriate language, but
shall not redefine the underlying semantics.

## Cancellation and Failure

Cancellation shall produce an explicit cancelled state.

Failure shall produce an explicit failed state or visible error.

An operation shall not simply stop updating while leaving the user unable to
determine whether it completed, failed, or was cancelled.

## Consequences

Progress reporting improves user confidence and reduces premature termination
of valid long-running operations.

Some workflows will require additional instrumentation to expose meaningful
work units.

## Non-Blocking Interaction

Long-running operations should execute independently of the interactive user
session where practical.

Users shall remain able to navigate the application and perform unrelated
permitted work while a long-running operation continues.

An operation may temporarily protect only those resources whose concurrent
modification would threaten correctness, consistency, or evidentiary
integrity. It shall not unnecessarily block the entire user interface,
account, or unrelated work.

Progress for independently executing operations shall remain visible so users
can return to the operation, inspect its status, and identify completion,
cancellation, or failure.

ADR-006 provides the durable checkpoint and recovery foundation for
long-running workflows. This ADR adds the user-facing liveness, progress, and
non-blocking interaction requirements.

## References

- ADR-001: Domain Independence
- ADR-002: The Universality Test
- ADR-006: Activity Results and Workflow Checkpoint Integration
- ADR-035: Core and Industry Extension Boundary
- ADR-042: Printable Source Evidence Preservation

## Architectural Principle

Long-running work must remain visibly alive, must not unnecessarily prevent
unrelated user work, and progress presented to users must describe measured
reality rather than estimated appearance.
