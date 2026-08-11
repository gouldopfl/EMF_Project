using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsVeteranMedicalConditionArtifactTests
{
    [Fact]
    public void Association_PreservesVeteranConditionArtifactAndRole()
    {
        var veteranId =
            new VeteranId("veteran-001");

        var medicalConditionId =
            new MedicalConditionId("medical-condition-001");

        var artifactId =
            new ArtifactId("artifact-001");

        var association =
            new VeteranMedicalConditionArtifact
            {
                VeteranId = veteranId,
                MedicalConditionId = medicalConditionId,
                ArtifactId = artifactId,
                Role =
                    ConditionRelationshipTraceabilityRoles.Supporting
            };

        Assert.Equal(
            veteranId,
            association.VeteranId);

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
