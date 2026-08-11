using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsServiceEventArtifactTests
{
    [Fact]
    public void Association_PreservesServiceEventArtifactAndRole()
    {
        var serviceEventId =
            new ServiceEventId("service-event-001");

        var artifactId =
            new ArtifactId("artifact-001");

        var association =
            new ServiceEventArtifact
            {
                ServiceEventId = serviceEventId,
                ArtifactId = artifactId,
                Role = ServiceEventTraceabilityRoles.Qualifying
            };

        Assert.Equal(serviceEventId, association.ServiceEventId);
        Assert.Equal(artifactId, association.ArtifactId);
        Assert.Equal(
            ServiceEventTraceabilityRoles.Qualifying,
            association.Role);
    }
}
