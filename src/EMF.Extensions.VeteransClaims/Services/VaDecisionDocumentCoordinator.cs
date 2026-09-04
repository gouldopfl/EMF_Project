using EMF.Common;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class VaDecisionDocumentCoordinator
{
    private readonly IClaimIssueRepository _issues;
    private readonly IConditionRepository _conditions;
    private readonly VaDecisionDocumentIssueMatchingService _matching;
    private readonly VaDecisionDocumentPersistenceService _persistence;
    private readonly VaDecisionDocumentProcessingAttemptService _attempts;
    private readonly IIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;

    public VaDecisionDocumentCoordinator(
        IClaimIssueRepository issues,
        IConditionRepository conditions,
        VaDecisionDocumentIssueMatchingService matching,
        VaDecisionDocumentPersistenceService persistence,
        VaDecisionDocumentProcessingAttemptService attempts,
        IIdGenerator idGenerator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(matching);
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _issues = issues;
        _conditions = conditions;
        _matching = matching;
        _persistence = persistence;
        _attempts = attempts;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
    }

    public async Task<VaDecisionDocumentProcessingResult> ProcessAsync(
        ClaimId claimId,
        VaDecisionDocumentInterpretation interpretation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interpretation);

        var claimIssues =
            await _issues.GetClaimIssuesAsync(
                claimId,
                cancellationToken);

        var mismatchedIssue =
            claimIssues.FirstOrDefault(
                x => x.ClaimId != claimId);

        if (mismatchedIssue is not null)
        {
            throw new InvalidOperationException(
                $"Claim '{claimId.Value}' lookup returned claim issue " +
                $"'{mismatchedIssue.Id.Value}' for claim " +
                $"'{mismatchedIssue.ClaimId.Value}'.");
        }

        var claimedConditions =
            new List<ClaimedCondition>();

        foreach (var issue in claimIssues)
        {
            var conditions =
                await _conditions.GetClaimedConditionsAsync(
                    issue.Id,
                    cancellationToken);

            var mismatchedCondition =
                conditions.FirstOrDefault(
                    x => x.ClaimIssueId != issue.Id);

            if (mismatchedCondition is not null)
            {
                throw new InvalidOperationException(
                    $"Claim issue '{issue.Id.Value}' lookup returned " +
                    $"condition '{mismatchedCondition.Id.Value}' for issue " +
                    $"'{mismatchedCondition.ClaimIssueId.Value}'.");
            }

            claimedConditions.AddRange(conditions);
        }

        var matches =
            _matching.Match(
                interpretation,
                claimedConditions);

        var unresolved =
            matches.Where(
                match =>
                    match.Status !=
                        VaDecisionDocumentIssueMatchStatuses.Matched ||
                    match.ClaimIssueId is null)
                .ToArray();

        if (unresolved.Length != 0)
        {
            var unresolvedResult =
                new VaDecisionDocumentProcessingResult
                {
                    Decision = null,
                    Matches = matches
                };

            await _attempts.RecordAsync(
                claimId,
                interpretation,
                unresolvedResult,
                _timeProvider.GetUtcNow(),
                cancellationToken);

            return unresolvedResult;
        }

        var decision =
            await _persistence.GetByArtifactAsync(
                interpretation.ArtifactId,
                cancellationToken);

        if (decision is null)
        {
            var matchedIssues =
                matches.Select(
                        match =>
                            new VaDecisionDocumentMatchedIssue
                            {
                                IssueDecisionId =
                                    new IssueDecisionId(
                                        _idGenerator.Generate()),
                                Match = match
                            })
                    .ToArray();

            decision =
                await _persistence.PersistAsync(
                    new PersistVaDecisionDocumentRequest
                    {
                        VaDecisionId =
                            new VaDecisionId(
                                _idGenerator.Generate()),
                        Interpretation = interpretation,
                        MatchedIssues = matchedIssues
                    },
                    cancellationToken);
        }

        var result =
            new VaDecisionDocumentProcessingResult
            {
                Decision = decision,
                Matches = matches
            };

        await _attempts.RecordAsync(
            claimId,
            interpretation,
            result,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        return result;
    }
}
