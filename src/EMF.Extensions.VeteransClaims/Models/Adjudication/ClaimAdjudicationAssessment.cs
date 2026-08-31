using EMF.Extensions.VeteransClaims.Models.Claims;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimAdjudicationAssessment
{
    public required Claim Claim { get; init; }

    public required IReadOnlyList<ClaimIssueAdjudicationAssessment>
        Issues { get; init; }

    public int IssueCount =>
        Issues.Count;

    public int ReadyIssueCount =>
        Issues.Count(x => x.Readiness.IsReadyForAdjudication);

    public int BlockedIssueCount =>
        Issues.Count(x => !x.Readiness.IsReadyForAdjudication);

    public bool RequiresAttention =>
        Issues.Any(x => x.RequiresAttention);

    public bool ShouldConsiderFollowUp =>
        Issues.Any(x => x.ShouldConsiderFollowUp);
}
