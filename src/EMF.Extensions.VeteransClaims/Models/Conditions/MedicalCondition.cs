using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Conditions;

public sealed class MedicalCondition
{
    public required MedicalConditionId Id { get; init; }

    public required string Name { get; init; }
}
