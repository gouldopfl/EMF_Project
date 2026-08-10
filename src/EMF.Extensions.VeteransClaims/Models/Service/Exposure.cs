using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class Exposure
{
    public required ExposureId Id { get; init; }

    public required VeteranId VeteranId { get; init; }

    public required string ExposureType { get; init; }
}
