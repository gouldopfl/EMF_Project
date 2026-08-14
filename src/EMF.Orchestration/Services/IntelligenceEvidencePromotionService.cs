using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class IntelligenceEvidencePromotionService :
    IIntelligenceEvidencePromotionService
{
    private readonly IEvidenceRepository _repository;

    public IntelligenceEvidencePromotionService(
        IEvidenceRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public Task PromoteAsync<TOutput>(
        IntelligenceEvidencePromotionRequest<TOutput> request,
        CancellationToken cancellationToken = default)
        where TOutput : notnull
    {
        IntelligenceEvidencePromotionValidator.Validate(
            request);

        var result = request.IntelligenceResult;

        var relationships =
            result.SourceArtifactIds
                .Distinct()
                .Select(sourceArtifactId =>
                    new Relationship
                    {
                        SourceArtifactId =
                            request.Artifact.Id,
                        TargetArtifactId =
                            sourceArtifactId,
                        RelationshipType =
                            RelationshipTypes.GeneratedFrom,
                        CreatedUtc = request.PromotedUtc,
                        Properties =
                            new Dictionary<string, object>
                            {
                                ["agentId"] =
                                    result.AgentId.Value,
                                ["correlationId"] =
                                    result.CorrelationId.Value
                            }
                    })
                .ToArray();

        return _repository
            .AddArtifactWithProvenanceAndRelationshipsAsync(
                request.Artifact,
                IntelligenceEvidenceProvenanceFactory.Create(
                    request),
                relationships,
                cancellationToken);
    }
}
