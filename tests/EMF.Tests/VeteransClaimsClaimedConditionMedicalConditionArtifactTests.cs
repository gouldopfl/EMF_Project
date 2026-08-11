using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsClaimedConditionMedicalConditionArtifactTests
{
    [Fact]
    public void Association_PreservesConditionRelationshipArtifactAndRole()
    {
        var claimedConditionId =
            new ClaimedConditionId("claimed-condition-001");

        var medicalConditionId =
            new MedicalConditionId("medical-condition-001");

        var artifactId =
            new ArtifactId("artifact-001");

        var association =
            new ClaimedConditionMedicalConditionArtifact
            {
                ClaimedConditionId = claimedConditionId,
                MedicalConditionId = medicalConditionId,
                ArtifactId = artifactId,
                Role =
                    ConditionRelationshipTraceabilityRoles.Supporting
            };

        Assert.Equal(
            claimedConditionId,
            association.ClaimedConditionId);

        Assert.Equal(
            medicalConditionId,
            association.MedicalConditionId);

        Assert.Equal(
            artifactId,
            association.ArtifactId);

        Assert.Equal(
            ConditionRelationshipTraceabilityRoles.Supporting,
            association.Role);
    }
}
