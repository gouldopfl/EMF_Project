using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Regulatory;

public sealed class Requirement
{
    public required RequirementId Id { get; init; }

    public required RegulatoryProvisionId RegulatoryProvisionId { get; init; }

    public required string Description { get; init; }
}
