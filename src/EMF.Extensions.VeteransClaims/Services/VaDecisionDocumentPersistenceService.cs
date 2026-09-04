using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class VaDecisionDocumentPersistenceService
{
    private readonly IVaDecisionRepository _repository;
    private readonly VaDecisionDocumentInterpretationValidator _validator;
    private readonly VaDecisionDocumentIssueDecisionFactory _factory;

    public VaDecisionDocumentPersistenceService(
        IVaDecisionRepository repository,
        VaDecisionDocumentInterpretationValidator validator,
        VaDecisionDocumentIssueDecisionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(factory);

        _repository = repository;
        _validator = validator;
        _factory = factory;
    }

    public Task<VaDecision?> GetByArtifactAsync(
        EMF.Core.Models.Identities.ArtifactId artifactId,
        CancellationToken cancellationToken = default) =>
        _repository.GetDecisionByArtifactAsync(
            artifactId,
            cancellationToken);

    public async Task<VaDecision> PersistAsync(
        PersistVaDecisionDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _validator.Validate(request.Interpretation);

        if (request.Interpretation.DecisionDate is null)
        {
            throw new InvalidOperationException(
                "A persisted VA decision document must contain a decision date.");
        }

        if (request.MatchedIssues.Count == 0)
        {
            throw new InvalidOperationException(
                "A persisted VA decision document must contain at least one matched issue.");
        }

        ArgumentNullException.ThrowIfNull(
            request.MatchedIssues);

        foreach (var matchedIssue in request.MatchedIssues)
        {
            ArgumentNullException.ThrowIfNull(matchedIssue);
            ArgumentNullException.ThrowIfNull(matchedIssue.Match);
        }

        if (request.MatchedIssues.Count !=
            request.Interpretation.IssueDecisions.Count)
        {
            throw new InvalidOperationException(
                "Every interpreted VA decision issue must have " +
                "exactly one matched issue.");
        }

        if (request.MatchedIssues
                .Select(x => x.IssueDecisionId)
                .Distinct()
                .Count() != request.MatchedIssues.Count)
        {
            throw new InvalidOperationException(
                "VA decision issue decision IDs must be unique.");
        }

        foreach (var matchedIssue in request.MatchedIssues)
        {
            if (matchedIssue.Match.Status !=
                    VaDecisionDocumentIssueMatchStatuses.Matched ||
                matchedIssue.Match.ClaimIssueId is null)
            {
                throw new InvalidOperationException(
                    "Only uniquely matched VA decision issues " +
                    "may be persisted.");
            }

            if (matchedIssue.Match.CandidateClaimIssueIds.Count != 1 ||
                matchedIssue.Match.CandidateClaimIssueIds[0] !=
                    matchedIssue.Match.ClaimIssueId.Value)
            {
                throw new InvalidOperationException(
                    "A matched VA decision issue must have exactly " +
                    "one matching claim issue candidate.");
            }
        }

        foreach (var interpretationIssue in
                 request.Interpretation.IssueDecisions)
        {
            var matchCount =
                request.MatchedIssues.Count(
                    x =>
                        ReferenceEquals(
                            x.Match.Interpretation,
                            interpretationIssue));

            if (matchCount != 1)
            {
                throw new InvalidOperationException(
                    "Every interpreted VA decision issue must be " +
                    "represented exactly once in the matched issues.");
            }
        }

        var decision =
            new VaDecision
            {
                Id = request.VaDecisionId,
                DecisionDate =
                    request.Interpretation.DecisionDate.Value
            };

        var issueDecisions =
            request.MatchedIssues
                .Select(
                    item =>
                        _factory.Create(
                            item.IssueDecisionId,
                            decision.Id,
                            item.Match))
                .ToArray();

        await _repository.AddDecisionDocumentAsync(
            decision,
            issueDecisions,
            new VaDecisionArtifact
            {
                VaDecisionId = decision.Id,
                ArtifactId =
                    request.Interpretation.ArtifactId
            },
            cancellationToken);

        return decision;
    }
}
