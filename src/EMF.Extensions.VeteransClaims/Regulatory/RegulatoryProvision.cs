using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Regulatory;

public sealed class RegulatoryProvision
{
    public required RegulatoryProvisionId Id { get; init; }

    public required RegulatoryAuthorityId RegulatoryAuthorityId { get; init; }

    public required string ProvisionType { get; init; }

    public required string Citation { get; init; }
}
