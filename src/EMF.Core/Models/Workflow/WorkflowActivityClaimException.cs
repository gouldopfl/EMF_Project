using EMF.Core.Models.Identities;

namespace EMF.Core.Models.Workflow;

public sealed class WorkflowActivityClaimException :
    InvalidOperationException
{
    public WorkflowActivityClaimException(
        WorkflowId workflowId,
        string activityId,
        string claimId,
        string message)
        : base(message)
    {
        WorkflowId = workflowId;
        ActivityId = activityId;
        ClaimId = claimId;
    }

    public WorkflowId WorkflowId { get; }

    public string ActivityId { get; }

    public string ClaimId { get; }
}
