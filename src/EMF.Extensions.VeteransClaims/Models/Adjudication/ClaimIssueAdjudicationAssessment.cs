namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueAdjudicationAssessment
{
    public required ClaimIssueAdjudicationDetails Details { get; init; }

    public required ClaimIssueAdjudicationReadiness Readiness { get; init; }
}
