using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EffectiveDate
{
    public required EffectiveDateId Id { get; init; }

    public required DisabilityEvaluationId DisabilityEvaluationId { get; init; }

    public required DateOnly Date { get; init; }
}
