using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsVaDecisionArtifactTests
{
    [Fact]
    public void Association_PreservesVaDecisionAndArtifact()
    {
        var vaDecisionId =
            new VaDecisionId("decision-001");

        var artifactId =
            new ArtifactId("artifact-001");

        var association =
            new VaDecisionArtifact
            {
                VaDecisionId = vaDecisionId,
                ArtifactId = artifactId
            };

        Assert.Equal(
            vaDecisionId,
            association.VaDecisionId);

        Assert.Equal(
            artifactId,
            association.ArtifactId);
    }
}
