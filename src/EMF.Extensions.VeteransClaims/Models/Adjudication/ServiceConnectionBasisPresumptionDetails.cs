using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ServiceConnectionBasisPresumptionDetails
{
    public required ServiceConnectionBasis Basis { get; init; }

    public required RegulatoryProvision PresumptionProvision { get; init; }
}
