using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Conditions;

public sealed class ClaimedConditionMedicalConditionArtifact
{
    public required ClaimedConditionId ClaimedConditionId
    {
        get;
        init;
    }

    public required MedicalConditionId MedicalConditionId
    {
        get;
        init;
    }

    public required ArtifactId ArtifactId { get; init; }

    public required string Role { get; init; }
}
