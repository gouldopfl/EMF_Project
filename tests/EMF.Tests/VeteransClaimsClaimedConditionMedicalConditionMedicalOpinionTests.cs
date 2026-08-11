using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsClaimedConditionMedicalConditionMedicalOpinionTests
{
    [Fact]
    public void Association_PreservesConditionRelationshipOpinionAndRole()
    {
        var claimedConditionId =
            new ClaimedConditionId("claimed-condition-001");

        var medicalConditionId =
            new MedicalConditionId("medical-condition-001");

        var medicalOpinionId =
            new MedicalOpinionId("medical-opinion-001");

        var association =
            new ClaimedConditionMedicalConditionMedicalOpinion
            {
                ClaimedConditionId = claimedConditionId,
                MedicalConditionId = medicalConditionId,
                MedicalOpinionId = medicalOpinionId,
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
            medicalOpinionId,
            association.MedicalOpinionId);

        Assert.Equal(
            ConditionRelationshipTraceabilityRoles.Supporting,
            association.Role);
    }
}
