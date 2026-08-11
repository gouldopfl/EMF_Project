using EMF.Extensions.VeteransClaims.Contracts;

namespace EMF.Extensions.VeteransClaims.Persistence;

public interface IVeteransClaimsPersistence
{
    IVeteranRepository Veterans { get; }

    IClaimRepository Claims { get; }

    IClaimIssueRepository ClaimIssues { get; }

    ISubmissionRepository Submissions { get; }

    IVaDecisionRepository Decisions { get; }

    IDisabilityEvaluationRepository
        DisabilityEvaluations { get; }

    Task InitializeAsync(
        CancellationToken cancellationToken = default);
}
