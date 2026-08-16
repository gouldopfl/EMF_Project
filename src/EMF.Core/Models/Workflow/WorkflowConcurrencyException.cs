using EMF.Core.Models.Identities;

namespace EMF.Core.Models.Workflow;

public sealed class WorkflowConcurrencyException :
    InvalidOperationException
{
    public WorkflowConcurrencyException(
        WorkflowId workflowId,
        long expectedRevision)
        : base(
            $"Workflow '{workflowId}' does not exist or " +
            $"revision {expectedRevision} is stale.")
    {
        WorkflowId = workflowId;
        ExpectedRevision = expectedRevision;
    }

    public WorkflowId WorkflowId { get; }

    public long ExpectedRevision { get; }
}
