namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueCurrentDecision
{
    public required IssueDecision IssueDecision { get; init; }

    public required VaDecision VaDecision { get; init; }
}
