using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsServiceConnectionBasisArtifactTests
{
    [Fact]
    public void Association_PreservesBasisArtifactAndRole()
    {
        var basisId =
            new ServiceConnectionBasisId("basis-001");

        var artifactId =
            new ArtifactId("artifact-001");

        var association =
            new ServiceConnectionBasisArtifact
            {
                ServiceConnectionBasisId = basisId,
                ArtifactId = artifactId,
                Role =
                    ServiceConnectionBasisTraceabilityRoles.Supporting
            };

        Assert.Equal(
            basisId,
            association.ServiceConnectionBasisId);

        Assert.Equal(
            artifactId,
            association.ArtifactId);

        Assert.Equal(
            ServiceConnectionBasisTraceabilityRoles.Supporting,
            association.Role);
    }
}
