using EMF.Core.Models;
using EMF.Intelligence.Agents;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransEvidenceSummaryPromotionService :
    IVeteransEvidenceSummaryPromotionService
{
    private readonly IIntelligenceEvidencePromotionService
        _promotionService;

    public VeteransEvidenceSummaryPromotionService(
        IIntelligenceEvidencePromotionService promotionService)
    {
        ArgumentNullException.ThrowIfNull(promotionService);

        _promotionService = promotionService;
    }

    public async Task<Artifact> PromoteAsync(
        string name,
        string promotedBy,
        string reviewedBy,
        DateTimeOffset promotedUtc,
        IntelligenceAgentResult<string> result,
        CancellationToken cancellationToken = default)
    {
        var artifact =
            new TextSummaryEvidenceArtifactFactory()
                .Create(
                    result.Output!,
                    name,
                    promotedUtc);

        await _promotionService.PromoteAsync(
            new IntelligenceEvidencePromotionRequest<string>
            {
                Artifact = artifact,
                IntelligenceResult = result,
                PromotedBy = promotedBy,
                PromotedUtc = promotedUtc,
                ReviewedBy = reviewedBy,
                ReviewedUtc = promotedUtc
            },
            cancellationToken);

        return artifact;
    }
}
