using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Conditions;

public sealed class ClaimedCondition
{
    public required ClaimedConditionId Id { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public required string Name { get; init; }
}
