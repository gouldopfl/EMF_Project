using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ServiceConnectionBasisArtifactDetails
{
    public required ServiceConnectionBasis Basis { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public required string Role { get; init; }
}
