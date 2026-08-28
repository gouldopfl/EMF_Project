using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueAdjudicationEvent
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required string EventType { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public string? ReferenceId { get; init; }

    public string? Outcome { get; init; }

    public string? Description { get; init; }
}
