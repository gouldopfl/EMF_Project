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
    private readonly IIdGenerator _idGenerator;

    public VaDecisionDocumentCoordinator(
        IClaimIssueRepository issues,
        IConditionRepository conditions,
        VaDecisionDocumentIssueMatchingService matching,
        VaDecisionDocumentPersistenceService persistence,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(matching);
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(idGenerator);

        _issues = issues;
        _conditions = conditions;
        _matching = matching;
        _persistence = persistence;
        _idGenerator = idGenerator;
    }

    public async Task<VaDecision> ProcessAsync(
        ClaimId claimId,
        VaDecisionDocumentInterpretation interpretation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interpretation);

        var claimIssues =
            await _issues.GetClaimIssuesAsync(
                claimId,
                cancellationToken);

        var claimedConditions =
            new List<ClaimedCondition>();

        foreach (var issue in claimIssues)
        {
            var conditions =
                await _conditions.GetClaimedConditionsAsync(
                    issue.Id,
                    cancellationToken);

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
            throw new InvalidOperationException(
                "The VA decision document contains unmatched or ambiguous issues.");
        }

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

        return await _persistence.PersistAsync(
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
}
