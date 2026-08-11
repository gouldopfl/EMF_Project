using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEffectiveDateArtifactTests
{
    [Fact]
    public void Association_PreservesEffectiveDateAndArtifact()
    {
        var effectiveDateId =
            new EffectiveDateId("effective-date-001");

        var artifactId =
            new ArtifactId("artifact-001");

        var association =
            new EffectiveDateArtifact
            {
                EffectiveDateId = effectiveDateId,
                ArtifactId = artifactId
            };

        Assert.Equal(
            effectiveDateId,
            association.EffectiveDateId);

        Assert.Equal(
            artifactId,
            association.ArtifactId);
    }
}
