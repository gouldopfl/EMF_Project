using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class SourceClarification
{
    public required SourceClarificationId Id { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public required ArtifactId SourceArtifactId { get; init; }

    public required DateOnly EvidenceDate { get; init; }

    public required int SourceStartPage { get; init; }

    public required int SourceEndPage { get; init; }

    public required string RecordTitle { get; init; }

    public required string Category { get; init; }

    public required string OriginalText { get; init; }

    public required string Clarification { get; init; }
}
