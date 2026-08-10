using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceConnectionBasisExposure
{
    public required ServiceConnectionBasisId ServiceConnectionBasisId { get; init; }

    public required ExposureId ExposureId { get; init; }
}
