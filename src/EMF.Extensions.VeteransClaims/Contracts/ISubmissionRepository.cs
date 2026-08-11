using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface ISubmissionRepository
{
    Task AddSubmissionAsync(
        Submission submission,
        IReadOnlyCollection<ClaimIssueId> claimIssueIds,
        CancellationToken cancellationToken = default);

    Task<Submission?> GetSubmissionAsync(
        SubmissionId submissionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Submission>> GetSubmissionsAsync(
        ClaimId claimId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClaimIssueId>>
        GetClaimIssueIdsAsync(
            SubmissionId submissionId,
            CancellationToken cancellationToken = default);
}
