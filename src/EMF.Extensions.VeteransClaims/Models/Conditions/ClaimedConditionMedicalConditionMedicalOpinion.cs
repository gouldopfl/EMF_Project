using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Conditions;

public sealed class ClaimedConditionMedicalConditionMedicalOpinion
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

    public required MedicalOpinionId MedicalOpinionId
    {
        get;
        init;
    }

    public required string Role { get; init; }
}
