using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Claims;

public sealed class ClaimIssue
{
    public required ClaimIssueId Id { get; init; }

    public required ClaimId ClaimId { get; init; }

    public required string ClaimIssueType { get; init; }
}
