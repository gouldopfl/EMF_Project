using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsExposureArtifactTests
{
    [Fact]
    public void Association_PreservesExposureArtifactAndRole()
    {
        var exposureId =
            new ExposureId("exposure-001");

        var artifactId =
            new ArtifactId("artifact-001");

        var association =
            new ExposureArtifact
            {
                ExposureId = exposureId,
                ArtifactId = artifactId,
                Role = ExposureTraceabilityRoles.Contradicting
            };

        Assert.Equal(exposureId, association.ExposureId);
        Assert.Equal(artifactId, association.ArtifactId);
        Assert.Equal(
            ExposureTraceabilityRoles.Contradicting,
            association.Role);
    }
}
