using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public interface IVeteransReviewerPackageIntelligenceService
{
    Task<IntelligenceAgentResult<string>>
        SummarizeAsync(
            ClaimIssueAdjudicationDetails details,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default);
}
