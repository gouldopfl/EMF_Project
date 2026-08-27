using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Conditions;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class VaDecisionDocumentIssueMatchingService
{
    private readonly VaDecisionDocumentIssueMatcher _matcher;

    public VaDecisionDocumentIssueMatchingService(
        VaDecisionDocumentIssueMatcher matcher)
    {
        ArgumentNullException.ThrowIfNull(matcher);

        _matcher = matcher;
    }

    public IReadOnlyList<VaDecisionDocumentIssueMatch> Match(
        VaDecisionDocumentInterpretation interpretation,
        IReadOnlyList<ClaimedCondition> claimedConditions)
    {
        ArgumentNullException.ThrowIfNull(interpretation);
        ArgumentNullException.ThrowIfNull(claimedConditions);

        return
            interpretation.IssueDecisions
                .Select(
                    issue =>
                        _matcher.Match(
                            issue,
                            claimedConditions))
                .ToArray();
    }
}
