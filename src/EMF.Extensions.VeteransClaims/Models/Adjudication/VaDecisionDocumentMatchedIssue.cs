using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class VaDecisionDocumentMatchedIssue
{
    public required IssueDecisionId IssueDecisionId { get; init; }

    public required VaDecisionDocumentIssueMatch Match { get; init; }
}
