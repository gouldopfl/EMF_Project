using EMF.Core.Contracts;
using EMF.Core.Models.Identities;

namespace EMF.Intelligence.Models;

public sealed class IntelligenceCapabilityResult<TOutput> :
    IOperationResult
    where TOutput : notnull
{
    public bool Success { get; init; }

    public string? Message { get; init; }

    public TOutput? Output { get; init; }

    public required IntelligenceExecutionMetadata
        Metadata { get; init; }

    public IReadOnlyList<ArtifactId> SourceArtifactIds
    { get; init; } = Array.Empty<ArtifactId>();

    public IReadOnlyList<string> Warnings
    { get; init; } = Array.Empty<string>();

    public bool RequiresReview { get; init; }
}
