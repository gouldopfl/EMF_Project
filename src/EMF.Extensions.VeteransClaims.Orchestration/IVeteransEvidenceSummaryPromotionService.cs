using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Core.Models;
using EMF.Intelligence.Agents;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public interface IVeteransEvidenceSummaryPromotionService
{
    Task<Artifact> PromoteAsync(
        string name,
        string promotedBy,
        string reviewedBy,
        DateTimeOffset promotedUtc,
        EvidenceGapId evidenceGapId,
        RequirementId requirementId,
        IntelligenceAgentResult<string> result,
        CancellationToken cancellationToken = default);

    Task<Artifact> PromoteAsync(
        string name,
        string promotedBy,
        string reviewedBy,
        DateTimeOffset promotedUtc,
        ClaimIssueId claimIssueId,
        IntelligenceAgentResult<string> result,
        CancellationToken cancellationToken = default);
}
