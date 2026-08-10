using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceConnectionBasisServiceConnectedCondition
{
    public required ServiceConnectionBasisId ServiceConnectionBasisId { get; init; }

    public required MedicalConditionId ServiceConnectedConditionId { get; init; }
}
