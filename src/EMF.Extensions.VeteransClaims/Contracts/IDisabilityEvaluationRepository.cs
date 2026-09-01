using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IDisabilityEvaluationRepository
{
    Task AddEvaluationsAsync(
        IssueDecisionId issueDecisionId,
        IReadOnlyCollection<DisabilityEvaluation>
            evaluations,
        IReadOnlyCollection<EffectiveDate>
            effectiveDates,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DisabilityEvaluation>>
        GetEvaluationsAsync(
            IssueDecisionId issueDecisionId,
            CancellationToken cancellationToken = default);

    Task<DisabilityEvaluation?> GetCurrentEvaluationAsync(
        IssueDecisionId issueDecisionId,
        CancellationToken cancellationToken = default);

    Task<EffectiveDate?> GetEffectiveDateAsync(
        DisabilityEvaluationId disabilityEvaluationId,
        CancellationToken cancellationToken = default);
}
