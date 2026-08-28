using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class VaDecisionDocumentProcessingAttempt
{
    public required ClaimId ClaimId { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public required DateTimeOffset ProcessedAt { get; init; }

    public VaDecisionId? VaDecisionId { get; init; }

    public required IReadOnlyList<VaDecisionDocumentIssueMatch>
        Matches { get; init; }

    public bool Persisted =>
        VaDecisionId is not null;

    public bool HasUnresolvedIssues =>
        Matches.Any(
            match =>
                match.Status !=
                    VaDecisionDocumentIssueMatchStatuses.Matched ||
                match.ClaimIssueId is null);
}
