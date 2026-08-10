using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceDevelopmentPlanArtifact
{
    public required EvidenceDevelopmentPlanId EvidenceDevelopmentPlanId { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public required string Role { get; init; }
}
