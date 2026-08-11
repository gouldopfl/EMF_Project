using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Conditions;

public sealed class VeteranMedicalConditionArtifact
{
    public required VeteranId VeteranId { get; init; }

    public required MedicalConditionId MedicalConditionId
    {
        get;
        init;
    }

    public required ArtifactId ArtifactId { get; init; }

    public required string Role { get; init; }
}
