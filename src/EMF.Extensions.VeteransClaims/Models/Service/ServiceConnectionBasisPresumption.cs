using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceConnectionBasisPresumption
{
    public required ServiceConnectionBasisId ServiceConnectionBasisId { get; init; }

    public required RegulatoryProvisionId PresumptionProvisionId { get; init; }
}
