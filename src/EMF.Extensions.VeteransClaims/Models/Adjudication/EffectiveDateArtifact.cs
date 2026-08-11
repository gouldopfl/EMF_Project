using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EffectiveDateArtifact
{
    public required EffectiveDateId EffectiveDateId
    {
        get;
        init;
    }

    public required ArtifactId ArtifactId { get; init; }
}
