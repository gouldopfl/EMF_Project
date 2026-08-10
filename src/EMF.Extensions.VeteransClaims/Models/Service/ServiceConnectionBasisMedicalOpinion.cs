using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceConnectionBasisMedicalOpinion
{
    public required ServiceConnectionBasisId ServiceConnectionBasisId
    {
        get;
        init;
    }

    public required MedicalOpinionId MedicalOpinionId { get; init; }

    public required string Role { get; init; }
}
