using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceConnectionBasisClaimedCondition
{
    public required ServiceConnectionBasisId ServiceConnectionBasisId { get; init; }

    public required ClaimedConditionId ClaimedConditionId { get; init; }
}
