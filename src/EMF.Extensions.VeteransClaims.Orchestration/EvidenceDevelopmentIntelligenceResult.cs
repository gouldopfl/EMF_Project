using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class EvidenceDevelopmentIntelligenceResult
{
    public required bool Succeeded { get; init; }

    public string? Summary { get; init; }

    public string? Message { get; init; }

    public required bool RequiresReview { get; init; }

    public required IntelligenceExecutionMetadata Metadata { get; init; }
}
