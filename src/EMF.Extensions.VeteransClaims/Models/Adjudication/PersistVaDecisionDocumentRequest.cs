using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class PersistVaDecisionDocumentRequest
{
    public required VaDecisionId VaDecisionId { get; init; }

    public required VaDecisionDocumentInterpretation
        Interpretation { get; init; }

    public required IReadOnlyList<VaDecisionDocumentMatchedIssue>
        MatchedIssues { get; init; }
}
