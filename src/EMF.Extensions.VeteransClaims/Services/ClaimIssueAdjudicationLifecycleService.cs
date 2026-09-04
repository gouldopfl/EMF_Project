using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationLifecycleService
{
    private readonly IVaDecisionRepository _decisions;
    private readonly ISubmissionRepository _submissions;

    public ClaimIssueAdjudicationLifecycleService(
        IVaDecisionRepository decisions,
        ISubmissionRepository submissions)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(submissions);

        _decisions = decisions;
        _submissions = submissions;
    }

    public async Task<IReadOnlyList<ClaimIssueAdjudicationLifecycleEntry>>
        GetAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        var issueDecisions =
            await _decisions.GetIssueDecisionsAsync(
                claimIssueId,
                cancellationToken);

        var mismatchedDecision =
            issueDecisions.FirstOrDefault(
                x => x.ClaimIssueId != claimIssueId);

        if (mismatchedDecision is not null)
        {
            throw new InvalidOperationException(
                $"Claim issue '{claimIssueId.Value}' lookup returned " +
                $"issue decision '{mismatchedDecision.Id.Value}' for " +
                $"claim issue '{mismatchedDecision.ClaimIssueId.Value}'.");
        }

        var entries =
            new List<ClaimIssueAdjudicationLifecycleEntry>();

        foreach (var issueDecision in issueDecisions)
        {
            var decision =
                await _decisions.GetDecisionAsync(
                    issueDecision.VaDecisionId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "VA decision could not be read.");

            var submissionIds =
                await _decisions.GetSubmissionIdsAsync(
                    issueDecision.Id,
                    cancellationToken);

            foreach (var submissionId in submissionIds)
            {
                var submission =
                    await _submissions.GetSubmissionAsync(
                        submissionId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Submission could not be read.");

                entries.Add(
                    new ClaimIssueAdjudicationLifecycleEntry
                    {
                        ClaimIssueId = claimIssueId,
                        Submission = submission,
                        IssueDecision = issueDecision,
                        VaDecision = decision
                    });
            }
        }

        return entries
            .OrderBy(x => x.VaDecision.DecisionDate)
            .ToArray();
    }
}
