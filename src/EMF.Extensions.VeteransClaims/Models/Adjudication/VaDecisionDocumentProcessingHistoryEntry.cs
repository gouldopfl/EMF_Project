using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class VaDecisionDocumentProcessingHistoryEntry
{
    public required ArtifactId ArtifactId { get; init; }
    public required DateTimeOffset ProcessedAt { get; init; }
    public VaDecisionId? VaDecisionId { get; init; }

    public required int MatchedIssueCount { get; init; }
    public required int UnmatchedIssueCount { get; init; }
    public required int AmbiguousIssueCount { get; init; }

    public bool Persisted =>
        VaDecisionId is not null;

    public bool HasUnresolvedIssues =>
        UnmatchedIssueCount > 0 ||
        AmbiguousIssueCount > 0;
}
