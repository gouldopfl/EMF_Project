using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class VaDecisionDocumentIssueDecisionFactory
{
    public IssueDecision Create(
        IssueDecisionId issueDecisionId,
        VaDecisionId vaDecisionId,
        VaDecisionDocumentIssueMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);

        if (match.Status !=
            VaDecisionDocumentIssueMatchStatuses.Matched ||
            match.ClaimIssueId is null)
        {
            throw new InvalidOperationException(
                "Only a uniquely matched VA decision issue can create an issue decision.");
        }

        return new IssueDecision
        {
            Id = issueDecisionId,
            VaDecisionId = vaDecisionId,
            ClaimIssueId = match.ClaimIssueId.Value,
            Outcome = match.Interpretation.Outcome
        };
    }
}
