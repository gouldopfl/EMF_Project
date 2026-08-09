namespace EMF.Core.Models.Workflow;

public enum RecoveryDecision
{
    Resume,
    Retry,
    RequireReview,
    Failed,
    Abandoned
}
