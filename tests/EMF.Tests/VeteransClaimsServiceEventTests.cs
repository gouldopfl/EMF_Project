using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsServiceEventTests
{
    [Fact]
    public void ServiceEvent_PreservesIdentityVeteranAndDescription()
    {
        var eventId = new ServiceEventId("service-event-001");
        var veteranId = new VeteranId("veteran-001");

        var serviceEvent = new ServiceEvent
        {
            Id = eventId,
            VeteranId = veteranId,
            Description = "Documented in-service event"
        };

        Assert.Equal(eventId, serviceEvent.Id);
        Assert.Equal(veteranId, serviceEvent.VeteranId);
        Assert.Equal(
            "Documented in-service event",
            serviceEvent.Description);
    }

    [Fact]
    public void Exposure_PreservesIdentityVeteranAndType()
    {
        var exposureId = new ExposureId("exposure-001");
        var veteranId = new VeteranId("veteran-001");

        var exposure = new Exposure
        {
            Id = exposureId,
            VeteranId = veteranId,
            ExposureType = "Environmental"
        };

        Assert.Equal(exposureId, exposure.Id);
        Assert.Equal(veteranId, exposure.VeteranId);
        Assert.Equal("Environmental", exposure.ExposureType);
    }

    [Fact]
    public void ServiceEventExposure_PreservesAssociation()
    {
        var eventId = new ServiceEventId("service-event-001");
        var exposureId = new ExposureId("exposure-001");

        var association = new ServiceEventExposure
        {
            ServiceEventId = eventId,
            ExposureId = exposureId
        };

        Assert.Equal(eventId, association.ServiceEventId);
        Assert.Equal(exposureId, association.ExposureId);
    }
}
