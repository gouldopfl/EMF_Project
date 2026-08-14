using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IIntelligenceEvidencePromotionService
{
    Task PromoteAsync<TOutput>(
        IntelligenceEvidencePromotionRequest<TOutput> request,
        CancellationToken cancellationToken = default)
        where TOutput : notnull;
}
