using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Conditions;

public sealed class ClaimedConditionMedicalCondition
{
    public required ClaimedConditionId ClaimedConditionId { get; init; }

    public required MedicalConditionId MedicalConditionId { get; init; }
}
