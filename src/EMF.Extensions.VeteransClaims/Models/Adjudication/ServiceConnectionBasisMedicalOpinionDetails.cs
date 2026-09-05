using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ServiceConnectionBasisMedicalOpinionDetails
{
    public required ServiceConnectionBasis Basis { get; init; }

    public required MedicalOpinion MedicalOpinion { get; init; }

    public required string Role { get; init; }
}
