using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class DisabilityEvaluation
{
    public required DisabilityEvaluationId Id { get; init; }

    public required IssueDecisionId IssueDecisionId { get; init; }

    public required string Evaluation { get; init; }
}
