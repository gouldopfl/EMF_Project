using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public interface IEvidenceDevelopmentIntelligenceCoordinator
{
    Task<IntelligenceAgentResult<string>> SummarizeAsync(
        EvidenceDevelopmentPlanId planId,
        EvidenceGapId evidenceGapId,
        IntelligenceExecutionContext context,
        CancellationToken cancellationToken = default);
}
