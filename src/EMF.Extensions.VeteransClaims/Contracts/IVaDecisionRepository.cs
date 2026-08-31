using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IVaDecisionRepository
{
    Task AddDecisionAsync(
        VaDecision decision,
        IReadOnlyCollection<IssueDecision> issueDecisions,
        IReadOnlyCollection<IssueDecisionSubmission>
            submissionAssociations,
        CancellationToken cancellationToken = default);

    Task<VaDecision?> GetDecisionAsync(
        VaDecisionId vaDecisionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IssueDecision>>
        GetIssueDecisionsAsync(
            VaDecisionId vaDecisionId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IssueDecision>>
        GetIssueDecisionsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubmissionId>>
        GetSubmissionIdsAsync(
            IssueDecisionId issueDecisionId,
            CancellationToken cancellationToken = default);

    Task AddDecisionArtifactAsync(
        VaDecisionArtifact association,
        CancellationToken cancellationToken = default);

    async Task AddDecisionDocumentAsync(
        VaDecision decision,
        IReadOnlyCollection<IssueDecision> issueDecisions,
        VaDecisionArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        await AddDecisionAsync(
            decision,
            issueDecisions,
            [],
            cancellationToken);

        await AddDecisionArtifactAsync(
            artifact,
            cancellationToken);
    }

    Task<IReadOnlyList<ArtifactId>>
        GetArtifactIdsAsync(
            VaDecisionId vaDecisionId,
            CancellationToken cancellationToken = default);
}
