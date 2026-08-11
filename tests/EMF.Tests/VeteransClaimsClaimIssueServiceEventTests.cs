using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsClaimIssueServiceEventTests
{
    [Fact]
    public void Association_PreservesClaimIssueAndServiceEvent()
    {
        var claimIssueId =
            new ClaimIssueId("claim-issue-001");

        var serviceEventId =
            new ServiceEventId("service-event-001");

        var association =
            new ClaimIssueServiceEvent
            {
                ClaimIssueId = claimIssueId,
                ServiceEventId = serviceEventId
            };

        Assert.Equal(
            claimIssueId,
            association.ClaimIssueId);

        Assert.Equal(
            serviceEventId,
            association.ServiceEventId);
    }
}
