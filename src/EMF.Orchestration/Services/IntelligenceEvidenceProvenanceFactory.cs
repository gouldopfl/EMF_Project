using EMF.Core.Models;
using EMF.Intelligence.Models;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

internal static class IntelligenceEvidenceProvenanceFactory
{
    public static Provenance Create<TOutput>(
        IntelligenceEvidencePromotionRequest<TOutput> request)
        where TOutput : notnull
    {
        var result = request.IntelligenceResult;

        var properties = new Dictionary<string, object>
        {
            ["agentId"] = result.AgentId.Value,
            ["correlationId"] = result.CorrelationId.Value,
            ["startedUtc"] = result.StartedUtc,
            ["completedUtc"] = result.CompletedUtc,
            ["requiresReview"] = result.RequiresReview,
            ["warnings"] = result.Warnings.ToArray(),
            ["capabilityExecutions"] =
                result.CapabilityExecutions
                    .Select(CreateExecutionProperties)
                    .ToArray()
        };

        if (request.ReviewedBy is not null)
        {
            properties["reviewedBy"] =
                request.ReviewedBy;
            properties["reviewedUtc"] =
                request.ReviewedUtc!.Value;
        }

        return new Provenance
        {
            ArtifactId = request.Artifact.Id,
            Source = "EMF.Intelligence",
            RecordedUtc = request.PromotedUtc,
            RecordedBy = request.PromotedBy,
            Properties = properties
        };
    }

    private static IReadOnlyDictionary<string, object>
        CreateExecutionProperties(
            IntelligenceExecutionMetadata execution)
    {
        var properties = new Dictionary<string, object>
        {
            ["capabilityId"] = execution.CapabilityId.Value,
            ["providerId"] = execution.ProviderId.Value,
            ["engineName"] = execution.EngineName,
            ["startedUtc"] = execution.StartedUtc,
            ["completedUtc"] = execution.CompletedUtc
        };

        if (execution.EngineVersion is not null)
            properties["engineVersion"] = execution.EngineVersion;

        if (execution.ProviderOperationId is not null)
            properties["providerOperationId"] =
                execution.ProviderOperationId;

        return properties;
    }
}
