using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests.TestInfrastructure;

public sealed class InMemoryEvidenceRecognitionTermRepository :
    IEvidenceRecognitionTermRepository
{
    private readonly List<EvidenceRecognitionTerm> _terms = [];

    public Task AddEvidenceRecognitionTermAsync(
        EvidenceRecognitionTerm term,
        CancellationToken cancellationToken = default)
    {
        _terms.Add(term);

        return Task.CompletedTask;
    }

    public Task<EvidenceRecognitionTerm?>
        GetEvidenceRecognitionTermAsync(
            EvidenceRecognitionTermId termId,
            CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _terms.SingleOrDefault(
                term => term.Id == termId));
    }

    public Task<IReadOnlyList<EvidenceRecognitionTerm>>
        GetEvidenceRecognitionTermsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EvidenceRecognitionTerm> result =
            _terms
                .Where(term =>
                    term.RequirementId == requirementId)
                .OrderBy(term => term.Id.Value)
                .ToArray();

        return Task.FromResult(result);
    }
}
