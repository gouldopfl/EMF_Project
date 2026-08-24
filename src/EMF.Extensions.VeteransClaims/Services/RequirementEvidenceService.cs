using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class RequirementEvidenceService :
    IRequirementEvidenceService
{
    private readonly IEvidenceClassificationRepository _repository;

    public RequirementEvidenceService(
        IEvidenceClassificationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public Task<IReadOnlyList<EvidenceClassification>>
        GetEvidenceAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
    {
        return _repository.GetEvidenceClassificationsAsync(
            requirementId,
            cancellationToken);
    }

    public async Task<RequirementEvidenceAssessment>
        AssessAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
    {
        var evidence =
            await GetEvidenceAsync(
                requirementId,
                cancellationToken);

        return new RequirementEvidenceAssessment
        {
            RequirementId = requirementId,
            Evidence = evidence
        };
    }
}
