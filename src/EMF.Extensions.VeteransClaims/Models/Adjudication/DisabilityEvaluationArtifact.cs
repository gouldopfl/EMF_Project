using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class DisabilityEvaluationArtifact
{
    public required DisabilityEvaluationId DisabilityEvaluationId
    {
        get;
        init;
    }

    public required ArtifactId ArtifactId { get; init; }
}
