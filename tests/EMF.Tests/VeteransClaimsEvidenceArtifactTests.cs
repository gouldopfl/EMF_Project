using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceArtifactTests
{
    [Fact]
    public void EvidenceGapArtifact_ReferencesPlatformArtifact()
    {
        var gapId =
            new EvidenceGapId("evidence-gap-001");

        var artifactId =
            new ArtifactId("artifact-001");

        var reference = new EvidenceGapArtifact
        {
            EvidenceGapId = gapId,
            ArtifactId = artifactId,
            Role = EvidenceDevelopmentRoles.Supporting
        };

        Assert.Equal(gapId, reference.EvidenceGapId);
        Assert.Equal(artifactId, reference.ArtifactId);
        Assert.Equal(
            EvidenceDevelopmentRoles.Supporting,
            reference.Role);
    }

    [Fact]
    public void EvidenceDevelopmentPlanArtifact_ReferencesPlatformArtifact()
    {
        var planId =
            new EvidenceDevelopmentPlanId("development-plan-001");

        var artifactId =
            new ArtifactId("artifact-002");

        var reference =
            new EvidenceDevelopmentPlanArtifact
            {
                EvidenceDevelopmentPlanId = planId,
                ArtifactId = artifactId,
                Role = EvidenceDevelopmentRoles.PotentiallyUseful
            };

        Assert.Equal(
            planId,
            reference.EvidenceDevelopmentPlanId);

        Assert.Equal(artifactId, reference.ArtifactId);

        Assert.Equal(
            EvidenceDevelopmentRoles.PotentiallyUseful,
            reference.Role);
    }
}
