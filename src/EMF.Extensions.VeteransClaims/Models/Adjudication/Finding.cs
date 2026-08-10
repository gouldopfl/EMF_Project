using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class Finding
{
    public required FindingId Id { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public RequirementId? RequirementId { get; init; }

    public required string Outcome { get; init; }

    public required string Description { get; init; }
}
