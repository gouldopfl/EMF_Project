using EMF.Core.Models.Identities;
using EMF.Intelligence.Models;

namespace EMF.Intelligence.Agents;

internal sealed class
    SegmentedTextSummarizationResult
{
    public required bool Success { get; init; }

    public string? Message { get; init; }

    public string? Output { get; init; }

    public IReadOnlyList<
        IntelligenceExecutionMetadata>
        CapabilityExecutions
    { get; init; } =
        Array.Empty<
            IntelligenceExecutionMetadata>();

    public IReadOnlyList<ArtifactId>
        SourceArtifactIds
    { get; init; } =
        Array.Empty<ArtifactId>();

    public IReadOnlyList<string> Warnings
    { get; init; } =
        Array.Empty<string>();

    public bool RequiresReview { get; init; }
}
