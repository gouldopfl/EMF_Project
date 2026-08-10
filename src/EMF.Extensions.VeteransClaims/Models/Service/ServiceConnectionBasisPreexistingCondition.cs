using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceConnectionBasisPreexistingCondition
{
    public required ServiceConnectionBasisId ServiceConnectionBasisId { get; init; }

    public required MedicalConditionId PreexistingConditionId { get; init; }
}
