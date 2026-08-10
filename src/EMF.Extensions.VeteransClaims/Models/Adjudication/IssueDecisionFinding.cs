using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class IssueDecisionFinding
{
    public required IssueDecisionId IssueDecisionId { get; init; }

    public required FindingId FindingId { get; init; }
}
