using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceEventExposure
{
    public required ServiceEventId ServiceEventId { get; init; }

    public required ExposureId ExposureId { get; init; }
}
