using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ServiceConnectionBasisMedicationDetails
{
    public required ServiceConnectionBasis Basis { get; init; }

    public required string MedicationName { get; init; }
}
