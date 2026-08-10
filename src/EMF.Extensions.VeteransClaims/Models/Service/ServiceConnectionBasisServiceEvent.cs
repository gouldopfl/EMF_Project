using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceConnectionBasisServiceEvent
{
    public required ServiceConnectionBasisId ServiceConnectionBasisId { get; init; }

    public required ServiceEventId ServiceEventId { get; init; }
}
