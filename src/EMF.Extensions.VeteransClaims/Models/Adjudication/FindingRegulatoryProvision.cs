using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class FindingRegulatoryProvision
{
    public required FindingId FindingId { get; init; }

    public required RegulatoryProvisionId RegulatoryProvisionId { get; init; }

    public required string Role { get; init; }
}
