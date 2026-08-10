using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Claims;

public sealed class Claim
{
    public required ClaimId Id { get; init; }

    public required VeteranId VeteranId { get; init; }
}
