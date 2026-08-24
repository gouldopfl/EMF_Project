using EMF.Common;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class EvidenceClassificationService :
    IEvidenceClassificationService
{
    private readonly IEvidenceClassificationRepository _repository;
    private readonly IIdGenerator _idGenerator;

    public EvidenceClassificationService(
        IEvidenceClassificationRepository repository,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(idGenerator);
        _repository = repository;
        _idGenerator = idGenerator;
    }

    public async Task<EvidenceClassification> ClassifyAsync(
        ArtifactId artifactId,
        string classification,
        ClaimIssueId? claimIssueId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classification);

        if (classification is not (
            EvidenceClassifications.MedicalEvidence or
            EvidenceClassifications.ServiceTreatmentRecord or
            EvidenceClassifications.ServiceRecord or
            EvidenceClassifications.LayEvidence or
            EvidenceClassifications.Examination or
            EvidenceClassifications.MedicalOpinion or
            EvidenceClassifications.AdjudicativeRecord))
        {
            throw new ArgumentException(
                $"Unsupported evidence classification '{classification}'.",
                nameof(classification));
        }

        var existing =
            await _repository.FindEvidenceClassificationAsync(
                artifactId,
                claimIssueId,
                classification,
                cancellationToken);

        if (existing is not null)
            return existing;

        var result = new EvidenceClassification
        {
            Id = new EvidenceClassificationId(_idGenerator.Generate()),
            ArtifactId = artifactId,
            ClaimIssueId = claimIssueId,
            Classification = classification
        };

        await _repository.AddEvidenceClassificationAsync(
            result,
            cancellationToken);

        return result;
    }

    public Task AssociateRequirementAsync(
        EvidenceClassificationId classificationId,
        RequirementId requirementId,
        CancellationToken cancellationToken = default)
    {
        return _repository.AddEvidenceClassificationRequirementAsync(
            new EvidenceClassificationRequirement
            {
                EvidenceClassificationId = classificationId,
                RequirementId = requirementId
            },
            cancellationToken);
    }


}
