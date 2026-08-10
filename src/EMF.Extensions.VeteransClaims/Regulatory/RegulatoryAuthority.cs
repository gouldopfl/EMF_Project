using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Regulatory;

public sealed class RegulatoryAuthority
{
    public required RegulatoryAuthorityId Id { get; init; }

    public required string AuthorityType { get; init; }

    public required string Citation { get; init; }

    public required string Title { get; init; }
}
