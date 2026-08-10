using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Claims;

public sealed class SubmissionClaimIssue
{
    public required SubmissionId SubmissionId { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }
}
