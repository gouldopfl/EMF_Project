using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceClassificationServiceEventTests
{
    [Fact]
    public void Association_PreservesClassificationAndServiceEvent()
    {
        var classificationId =
            new EvidenceClassificationId("classification-001");

        var serviceEventId =
            new ServiceEventId("service-event-001");

        var association =
            new EvidenceClassificationServiceEvent
            {
                EvidenceClassificationId = classificationId,
                ServiceEventId = serviceEventId
            };

        Assert.Equal(
            classificationId,
            association.EvidenceClassificationId);

        Assert.Equal(
            serviceEventId,
            association.ServiceEventId);
    }
}
