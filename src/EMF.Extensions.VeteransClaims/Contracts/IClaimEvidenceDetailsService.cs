using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IClaimEvidenceDetailsService
{
    Task<ClaimEvidenceDetails?>
        GetAsync(
            ClaimId claimId,
            CancellationToken cancellationToken = default);
}
