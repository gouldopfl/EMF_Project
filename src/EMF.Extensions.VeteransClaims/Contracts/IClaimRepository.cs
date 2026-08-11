using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IClaimRepository
{
    Task AddClaimAsync(
        Claim claim,
        CancellationToken cancellationToken = default);

    Task<Claim?> GetClaimAsync(
        ClaimId claimId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Claim>> GetClaimsAsync(
        VeteranId veteranId,
        CancellationToken cancellationToken = default);
}
