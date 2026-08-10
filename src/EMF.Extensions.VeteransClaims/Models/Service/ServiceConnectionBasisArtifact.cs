using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceConnectionBasisArtifact
{
    public required ServiceConnectionBasisId ServiceConnectionBasisId
    {
        get;
        init;
    }

    public required ArtifactId ArtifactId { get; init; }

    public required string Role { get; init; }
}
