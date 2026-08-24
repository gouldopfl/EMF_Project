using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class EvidenceRecognitionMatcher
{
    private readonly IEvidenceRecognitionTermRepository _repository;

    public EvidenceRecognitionMatcher(
        IEvidenceRecognitionTermRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    public async Task<IReadOnlyList<EvidenceRecognitionMatch>>
        FindMatchesAsync(
            RequirementId requirementId,
            string text,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var terms =
            await _repository.GetEvidenceRecognitionTermsAsync(
                requirementId,
                cancellationToken);

        var matches = new List<EvidenceRecognitionMatch>();

        foreach (var term in terms)
        {
            if (text.Contains(
                    term.Term,
                    StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(
                    new EvidenceRecognitionMatch
                    {
                        TermId = term.Id,
                        Term = term.Term,
                        RecognitionRole =
                            term.RecognitionRole,
                        EvidenceClassification =
                            term.EvidenceClassification,
                        AuthoritySource =
                            term.AuthoritySource
                    });
            }
        }

        return matches;
    }
}
