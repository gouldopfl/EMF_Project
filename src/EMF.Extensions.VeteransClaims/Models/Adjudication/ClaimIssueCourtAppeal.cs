using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueCourtAppeal
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required string Court { get; init; }

    public required DateTimeOffset FiledAt { get; init; }

    public string? DocketNumber { get; init; }

    public string? Outcome { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }
}
