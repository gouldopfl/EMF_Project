using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsServiceConnectionBasisServiceEventTests
{
    [Fact]
    public void Association_PreservesBasisAndServiceEvent()
    {
        var basisId =
            new ServiceConnectionBasisId("basis-001");

        var serviceEventId =
            new ServiceEventId("service-event-001");

        var association =
            new ServiceConnectionBasisServiceEvent
            {
                ServiceConnectionBasisId = basisId,
                ServiceEventId = serviceEventId
            };

        Assert.Equal(
            basisId,
            association.ServiceConnectionBasisId);

        Assert.Equal(
            serviceEventId,
            association.ServiceEventId);
    }
}
