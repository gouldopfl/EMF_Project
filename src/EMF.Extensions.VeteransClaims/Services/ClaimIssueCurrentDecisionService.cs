using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueCurrentDecisionService
{
    private readonly IVaDecisionRepository _decisions;

    public ClaimIssueCurrentDecisionService(
        IVaDecisionRepository decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        _decisions = decisions;
    }

    public async Task<ClaimIssueCurrentDecision?> GetAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default)
    {
        var decisions =
            await _decisions.GetIssueDecisionsAsync(
                claimIssueId,
                cancellationToken);

        var mismatchedDecision =
            decisions.FirstOrDefault(
                x => x.ClaimIssueId != claimIssueId);

        if (mismatchedDecision is not null)
        {
            throw new InvalidOperationException(
                $"Claim issue '{claimIssueId.Value}' lookup returned " +
                $"issue decision '{mismatchedDecision.Id.Value}' for " +
                $"claim issue '{mismatchedDecision.ClaimIssueId.Value}'.");
        }

        if (decisions.Count == 0)
            return null;

        if (decisions.Count == 1)
        {
            var issueDecision = decisions[0];

            var vaDecision =
                await _decisions.GetDecisionAsync(
                    issueDecision.VaDecisionId,
                    cancellationToken);

            if (vaDecision is null)
            {
                throw new InvalidOperationException(
                    "VA decision could not be read.");
            }

            return new ClaimIssueCurrentDecision
            {
                IssueDecision = issueDecision,
                VaDecision = vaDecision
            };
        }

        var resolved =
            new List<(
                IssueDecision IssueDecision,
                VaDecision VaDecision)>();

        foreach (var issueDecision in decisions)
        {
            var vaDecision =
                await _decisions.GetDecisionAsync(
                    issueDecision.VaDecisionId,
                    cancellationToken);

            if (vaDecision is null)
            {
                throw new InvalidOperationException(
                    "VA decision could not be read.");
            }

            resolved.Add(
                (issueDecision, vaDecision));
        }

        var latestDate =
            resolved.Max(
                x => x.VaDecision.DecisionDate);

        var latest =
            resolved
                .Where(
                    x => x.VaDecision.DecisionDate == latestDate)
                .ToArray();

        if (latest.Length != 1)
        {
            throw new InvalidOperationException(
                "Current VA decision is ambiguous.");
        }

        return new ClaimIssueCurrentDecision
        {
            IssueDecision = latest[0].IssueDecision,
            VaDecision = latest[0].VaDecision
        };
    }
}
