using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class IssueDecision
{
    public required IssueDecisionId Id { get; init; }

    public required VaDecisionId VaDecisionId { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public required string Outcome { get; init; }
}
