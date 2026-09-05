using EMF.Core.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerEvidenceSource
{
    public required ArtifactId ArtifactId { get; init; }

    public required IReadOnlyList<string> Classifications { get; init; }

    public required string Text { get; init; }
}
