using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Agents;

public sealed class IntelligenceAgentResult<TOutput> :
    IOperationResult
    where TOutput : notnull
{
    public bool Success { get; init; }

    public string? Message { get; init; }

    public TOutput? Output { get; init; }

    public required AgentId AgentId { get; init; }

    public required IntelligenceCorrelationId
        CorrelationId { get; init; }

    public required DateTimeOffset StartedUtc
    { get; init; }

    public required DateTimeOffset CompletedUtc
    { get; init; }

    public IReadOnlyList<
        IntelligenceExecutionMetadata>
        CapabilityExecutions
    { get; init; } =
        Array.Empty<
            IntelligenceExecutionMetadata>();

    public IReadOnlyList<ArtifactId> SourceArtifactIds
    { get; init; } = Array.Empty<ArtifactId>();

    public IReadOnlyList<string> Warnings
    { get; init; } = Array.Empty<string>();

    public bool RequiresReview { get; init; }
}
