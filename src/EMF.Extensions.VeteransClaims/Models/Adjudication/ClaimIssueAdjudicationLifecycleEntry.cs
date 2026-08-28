using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueAdjudicationLifecycleEntry
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required Submission Submission { get; init; }

    public required IssueDecision IssueDecision { get; init; }

    public required VaDecision VaDecision { get; init; }
}
