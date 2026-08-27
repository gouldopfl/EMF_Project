using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class VaDecisionDocumentIssueMatcher
{
    public VaDecisionDocumentIssueMatch Match(
        VaIssueDecisionInterpretation interpretation,
        IReadOnlyList<ClaimedCondition> claimedConditions)
    {
        ArgumentNullException.ThrowIfNull(interpretation);
        ArgumentNullException.ThrowIfNull(claimedConditions);

        var description = Normalize(interpretation.IssueDescription);

        var candidates =
            claimedConditions
                .Where(
                    condition =>
                        Normalize(condition.Name) == description)
                .Select(condition => condition.ClaimIssueId)
                .Distinct()
                .ToArray();

        var status =
            candidates.Length switch
            {
                0 => VaDecisionDocumentIssueMatchStatuses.Unmatched,
                1 => VaDecisionDocumentIssueMatchStatuses.Matched,
                _ => VaDecisionDocumentIssueMatchStatuses.Ambiguous
            };

        return new VaDecisionDocumentIssueMatch
        {
            Interpretation = interpretation,
            Status = status,
            ClaimIssueId =
                candidates.Length == 1
                    ? candidates[0]
                    : null,
            CandidateClaimIssueIds = candidates
        };
    }

    private static string Normalize(string value)
    {
        return string.Join(
            ' ',
            value.Trim()
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }
}
