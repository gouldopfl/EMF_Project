using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsMedicalOpinionTraceabilityTests
{
    [Fact]
    public void MedicalOpinionArtifact_ReferencesPlatformArtifact()
    {
        var opinionId = new MedicalOpinionId("opinion-001");
        var artifactId = new ArtifactId("artifact-001");

        var reference = new MedicalOpinionArtifact
        {
            MedicalOpinionId = opinionId,
            ArtifactId = artifactId
        };

        Assert.Equal(opinionId, reference.MedicalOpinionId);
        Assert.Equal(artifactId, reference.ArtifactId);
    }
}
