using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidenceGapService
{
    Task<EvidenceGap?> EnsureGapAsync(
        ClaimIssueId claimIssueId,
        RequirementId requirementId,
        CancellationToken cancellationToken = default);
}
