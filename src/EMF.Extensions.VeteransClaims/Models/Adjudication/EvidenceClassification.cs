using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceClassification
{
    public required EvidenceClassificationId Id { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public ClaimIssueId? ClaimIssueId { get; init; }

    public required string Classification { get; init; }
}
