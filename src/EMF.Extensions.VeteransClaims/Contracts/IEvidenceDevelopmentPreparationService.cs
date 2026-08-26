using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidenceDevelopmentPreparationService
{
    Task<EvidenceDevelopmentPlanDetails?> PrepareAsync(
        EvidenceDevelopmentPlanId planId,
        ClaimIssueId claimIssueId,
        string description,
        CancellationToken cancellationToken = default);
}
