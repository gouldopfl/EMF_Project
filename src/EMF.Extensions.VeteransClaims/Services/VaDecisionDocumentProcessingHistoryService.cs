using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class VaDecisionDocumentProcessingHistoryService
{
    private readonly IVaDecisionDocumentProcessingAttemptRepository
        _attempts;

    public VaDecisionDocumentProcessingHistoryService(
        IVaDecisionDocumentProcessingAttemptRepository attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);

        _attempts = attempts;
    }

    public async Task<IReadOnlyList<VaDecisionDocumentProcessingHistoryEntry>>
        GetAsync(
            ClaimId claimId,
            CancellationToken cancellationToken = default)
    {
        var attempts =
            await _attempts.GetByClaimAsync(
                claimId,
                cancellationToken);

        if (attempts.Any(x => x.ClaimId != claimId))
            throw new InvalidOperationException(
                "Processing attempt claim mismatch.");

        return attempts
            .Select(
                attempt =>
                    new VaDecisionDocumentProcessingHistoryEntry
                    {
                        ArtifactId = attempt.ArtifactId,
                        ProcessedAt = attempt.ProcessedAt,
                        VaDecisionId = attempt.VaDecisionId,
                        MatchedIssueCount =
                            attempt.Matches.Count(
                                match =>
                                    match.Status ==
                                    VaDecisionDocumentIssueMatchStatuses.Matched),
                        UnmatchedIssueCount =
                            attempt.Matches.Count(
                                match =>
                                    match.Status ==
                                    VaDecisionDocumentIssueMatchStatuses.Unmatched),
                        AmbiguousIssueCount =
                            attempt.Matches.Count(
                                match =>
                                    match.Status ==
                                    VaDecisionDocumentIssueMatchStatuses.Ambiguous)
                    })
            .ToArray();
    }
}
