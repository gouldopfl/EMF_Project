# ADR-002: Workflow and Evidence Separation

## Status

Accepted

## Context

EMF processes long-running evidence operations including discovery,
inventory, fingerprinting, analysis, and persistence.

The system requires both:

- Durable evidence records
- Operational execution state

These two concepts are related but have different lifecycles and responsibilities.

Evidence represents knowledge that should remain available after processing
completes.

Workflow represents the execution process used to create, transform, or analyze
that evidence.

## Decision

EMF will maintain separate domains for workflow state and evidence state.

### Evidence Domain

Evidence represents durable knowledge.

Includes:

- Artifacts
- Relationships
- Provenance
- Content fingerprints
- Evidence metadata

Managed through:

- IEvidenceRepository

### Workflow Domain

Workflow represents operational execution.

Includes:

- Workflow identity
- Current status
- Processing checkpoints
- Resume information
- Execution progress

Managed through:

- IWorkflowRepository (future)

## Relationship Between Domains

Workflows may create, modify, or analyze evidence.

Evidence does not belong to a workflow.

A single evidence artifact may participate in multiple workflows.

Example:

A medical document may be used by:

- Claim preparation workflow
- Research workflow
- Archive migration workflow

The evidence remains independent.

## Consequences

### Benefits

- Evidence survives workflow changes.
- Failed workflows can resume without rebuilding evidence.
- Multiple workflows can operate on the same evidence.
- Storage implementations remain replaceable.
- Long-running operations become recoverable.

### Tradeoff

The architecture contains two separate concepts:

- Evidence lifecycle
- Workflow lifecycle

This additional separation is intentional.

## Principle

Workflow state is operational metadata.

Evidence state is durable knowledge.

They must be related, but not merged.
