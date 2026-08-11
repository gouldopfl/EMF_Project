using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Conditions;

public sealed class VeteranMedicalCondition
{
    public required VeteranId VeteranId { get; init; }

    public required MedicalConditionId MedicalConditionId { get; init; }
}
