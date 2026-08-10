using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class FindingArtifact
{
    public required FindingId FindingId { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public required string Role { get; init; }
}
