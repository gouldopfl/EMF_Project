using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IVaDecisionDocumentProcessingAttemptRepository
{
    Task AddAsync(
        VaDecisionDocumentProcessingAttempt attempt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VaDecisionDocumentProcessingAttempt>>
        GetByClaimAsync(
            ClaimId claimId,
            CancellationToken cancellationToken = default);
}
