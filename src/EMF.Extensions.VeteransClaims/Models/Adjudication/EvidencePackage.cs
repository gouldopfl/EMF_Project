using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidencePackage
{
    public required EvidencePackageId Id { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public required string Purpose { get; init; }

    public required string ReviewerRole { get; init; }
}
