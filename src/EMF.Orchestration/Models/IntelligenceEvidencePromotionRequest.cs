using EMF.Core.Models;
using EMF.Intelligence.Agents;

namespace EMF.Orchestration.Models;

public sealed class IntelligenceEvidencePromotionRequest<TOutput>
    where TOutput : notnull
{
    public required Artifact Artifact { get; init; }

    public required IntelligenceAgentResult<TOutput>
        IntelligenceResult { get; init; }

    public required string PromotedBy { get; init; }

    public required DateTimeOffset PromotedUtc { get; init; }

    public string? ReviewedBy { get; init; }

    public DateTimeOffset? ReviewedUtc { get; init; }
}
