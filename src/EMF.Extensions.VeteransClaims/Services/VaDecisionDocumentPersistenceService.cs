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
