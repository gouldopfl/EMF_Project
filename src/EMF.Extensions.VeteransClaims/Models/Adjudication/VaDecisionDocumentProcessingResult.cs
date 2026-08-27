namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class VaDecisionDocumentProcessingResult
{
    public VaDecision? Decision { get; init; }

    public required IReadOnlyList<VaDecisionDocumentIssueMatch>
        Matches { get; init; }

    public bool Persisted =>
        Decision is not null;

    public bool HasUnresolvedIssues =>
        Matches.Any(
            match =>
                match.Status !=
                    VaDecisionDocumentIssueMatchStatuses.Matched ||
                match.ClaimIssueId is null);
}
