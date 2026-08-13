using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidenceDevelopmentPlanRepository
{
    Task AddEvidenceDevelopmentPlanAsync(
        EvidenceDevelopmentPlan plan,
        CancellationToken cancellationToken = default);

    Task<EvidenceDevelopmentPlan?> GetEvidenceDevelopmentPlanAsync(
        EvidenceDevelopmentPlanId planId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceDevelopmentPlan>>
        GetEvidenceDevelopmentPlansAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default);
}
