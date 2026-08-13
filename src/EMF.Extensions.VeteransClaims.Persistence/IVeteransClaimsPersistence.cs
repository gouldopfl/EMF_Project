using EMF.Extensions.VeteransClaims.Contracts;

namespace EMF.Extensions.VeteransClaims.Persistence;

public interface IVeteransClaimsPersistence
{
    IVeteranRepository Veterans { get; }

    IClaimRepository Claims { get; }

    IClaimIssueRepository ClaimIssues { get; }

    IConditionRepository Conditions { get; }

    IRegulatoryRepository Regulatory { get; }

    IMedicalOpinionRepository MedicalOpinions { get; }

    IEvidenceClassificationRepository EvidenceClassifications { get; }

    IFindingRepository Findings { get; }

    IEvidenceGapRepository EvidenceGaps { get; }

    IServiceConnectionRepository ServiceConnections
    {
        get;
    }

    IServiceHistoryRepository ServiceHistory { get; }

    ISubmissionRepository Submissions { get; }

    IVaDecisionRepository Decisions { get; }

    IDisabilityEvaluationRepository
        DisabilityEvaluations
    { get; }

    Task InitializeAsync(
        CancellationToken cancellationToken = default);
}
