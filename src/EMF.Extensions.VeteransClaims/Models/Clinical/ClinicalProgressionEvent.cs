using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Clinical;

public sealed class ClinicalProgressionEvent
{
    public required ClinicalProgressionEventId Id { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public required ArtifactId SourceArtifactId { get; init; }

    public required DateOnly EventDate { get; init; }

    public int? SourceStartPage { get; init; }

    public int? SourceEndPage { get; init; }

    public required string RecordTitle { get; init; }

    public required string EventType { get; init; }

    public required string Summary { get; init; }
}
