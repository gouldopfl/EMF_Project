using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ClaimIssueServiceEvent
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required ServiceEventId ServiceEventId { get; init; }
}
