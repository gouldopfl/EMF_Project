using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueAdjudicationAging
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required DateTimeOffset PendingSince { get; init; }

    public required int AgeInDays { get; init; }

    public DateTimeOffset? LastActivityAt { get; init; }

    public required int DaysSinceLastActivity { get; init; }
}
