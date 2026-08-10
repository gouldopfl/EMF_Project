using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceEvent
{
    public required ServiceEventId Id { get; init; }

    public required VeteranId VeteranId { get; init; }

    public required string Description { get; init; }
}
