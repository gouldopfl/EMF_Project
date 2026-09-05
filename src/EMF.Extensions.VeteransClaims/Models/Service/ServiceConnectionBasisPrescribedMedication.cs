using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceConnectionBasisPrescribedMedication
{
    public required ServiceConnectionBasisId ServiceConnectionBasisId { get; init; }

    public required string MedicationName { get; init; }
}
