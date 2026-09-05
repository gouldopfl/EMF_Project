using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ServiceConnectionBasisExposureDetails
{
    public required ServiceConnectionBasis Basis { get; init; }

    public required Exposure Exposure { get; init; }
}
