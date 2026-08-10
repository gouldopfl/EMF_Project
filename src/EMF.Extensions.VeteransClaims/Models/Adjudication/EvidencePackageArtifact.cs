using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidencePackageArtifact
{
    public required EvidencePackageId EvidencePackageId { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public required string ContentRole { get; init; }
}
