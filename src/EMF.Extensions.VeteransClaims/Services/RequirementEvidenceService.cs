using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class RequirementEvidenceService :
    IRequirementEvidenceService
{
    private readonly IEvidenceClassificationRepository _repository;
    private readonly IEvidenceRequirementGuidanceRepository? _guidanceRepository;

    public RequirementEvidenceService(
        IEvidenceClassificationRepository repository)
        : this(repository, null)
    {
    }

    public RequirementEvidenceService(
        IEvidenceClassificationRepository repository,
        IEvidenceRequirementGuidanceRepository? guidanceRepository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
        _guidanceRepository = guidanceRepository;
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

    public async Task<RequirementEvidenceResponsivenessAssessment>
        AssessResponsivenessAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
    {
        if (_guidanceRepository is null)
        {
            throw new InvalidOperationException(
                "Evidence requirement guidance is not configured.");
        }

        var evidence =
            await GetEvidenceAsync(
                requirementId,
                cancellationToken);

        var guidance =
            await _guidanceRepository
                .GetEvidenceRequirementGuidanceAsync(
                    requirementId,
                    cancellationToken);

        var items =
            guidance
                .Select(
                    item =>
                        new RequirementEvidenceResponsivenessItem
                        {
                            Guidance = item,
                            HasMatchingEvidence =
                                evidence.Any(
                                    x =>
                                        x.Classification ==
                                        item.EvidenceClassification)
                        })
                .ToArray();

        return new RequirementEvidenceResponsivenessAssessment
        {
            RequirementId = requirementId,
            Items = items
        };
    }

    public async Task<EvidenceDevelopmentChecklist>
        CreateChecklistAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
    {
        var assessment =
            await AssessResponsivenessAsync(
                requirementId,
                cancellationToken);

        var items =
            assessment.MissingItems
                .Select(
                    item =>
                        new EvidenceDevelopmentChecklistItem
                        {
                            RequirementId = requirementId,
                            EvidenceClassification =
                                item.Guidance.EvidenceClassification,
                            GuidanceRole =
                                item.Guidance.GuidanceRole,
                            Description =
                                item.Guidance.Description
                        })
                .ToArray();

        return new EvidenceDevelopmentChecklist
        {
            RequirementId = requirementId,
            Items = items
        };
    }

}
