using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsVeteranMedicalConditionMedicalOpinionTests
{
    [Fact]
    public void Association_PreservesVeteranConditionOpinionAndRole()
    {
        var veteranId =
            new VeteranId("veteran-001");

        var medicalConditionId =
            new MedicalConditionId("medical-condition-001");

        var medicalOpinionId =
            new MedicalOpinionId("medical-opinion-001");

        var association =
            new VeteranMedicalConditionMedicalOpinion
            {
                VeteranId = veteranId,
                MedicalConditionId = medicalConditionId,
                MedicalOpinionId = medicalOpinionId,
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
            medicalOpinionId,
            association.MedicalOpinionId);

        Assert.Equal(
            ConditionRelationshipTraceabilityRoles.Supporting,
            association.Role);
    }
}
