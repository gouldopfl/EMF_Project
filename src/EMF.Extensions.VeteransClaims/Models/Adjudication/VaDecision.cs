using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class VaDecision
{
    public required VaDecisionId Id { get; init; }

    public DateTimeOffset DecisionDate { get; init; }
}
