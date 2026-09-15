using EMF.Core.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerEvidenceProjection
{
    public required ArtifactId ArtifactId { get; init; }

    public required string Text { get; init; }

    public required IReadOnlyList<int> SourceLineNumbers { get; init; }

    public required IReadOnlyList<string> MatchedTerms { get; init; }
}
