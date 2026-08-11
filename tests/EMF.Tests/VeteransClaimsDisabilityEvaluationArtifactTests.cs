using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsDisabilityEvaluationArtifactTests
{
    [Fact]
    public void Association_PreservesEvaluationAndArtifact()
    {
        var disabilityEvaluationId =
            new DisabilityEvaluationId("evaluation-001");

        var artifactId =
            new ArtifactId("artifact-001");

        var association =
            new DisabilityEvaluationArtifact
            {
                DisabilityEvaluationId =
                    disabilityEvaluationId,
                ArtifactId = artifactId
            };

        Assert.Equal(
            disabilityEvaluationId,
            association.DisabilityEvaluationId);

        Assert.Equal(
            artifactId,
            association.ArtifactId);
    }
}
