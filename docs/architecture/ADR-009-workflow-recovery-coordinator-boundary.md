# ADR-009: Workflow Recovery Coordinator Boundary

## Status

Proposed

## Context

ADR-007 introduced persisted workflow recovery state.

ADR-008 introduced the workflow recovery decision policy.

The workflow engine can now:

- identify interrupted executions
- evaluate recovery decisions

The next design question is where recovery coordination belongs.

The WorkflowRunner is responsible for executing workflow activities. Adding recovery orchestration directly into the runner would combine execution and recovery responsibilities.

## Decision

EMF will introduce a WorkflowRecoveryCoordinator responsible for coordinating recovery operations.

The coordinator will:

- load workflow execution state
- retrieve checkpoint history
- evaluate recovery policy
- record recovery decisions
- provide recovery direction to the workflow execution layer

The WorkflowRunner remains responsible for:

- executing workflow activities
- maintaining execution order
- recording activity progress

## Architecture

WorkflowRunner

        |

        v

WorkflowRecoveryCoordinator

        |

        +--> IWorkflowRecoveryPolicy

        |

        +--> WorkflowRepository


## Consequences

Benefits:

- workflow execution remains focused
- recovery logic evolves independently
- recovery decisions become auditable
- future recovery strategies can be added without changing execution logic

Tradeoff:

The platform introduces an additional orchestration component.

## Future Considerations

The coordinator may later support:

- automatic retry policies
- human approval workflows
- recovery history tracking
- multi-agent recovery decisions
