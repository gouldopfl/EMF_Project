using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ServiceConnectionBasisConditionDetails
{
    public required ServiceConnectionBasis Basis { get; init; }

    public required MedicalCondition ServiceConnectedCondition { get; init; }
}
