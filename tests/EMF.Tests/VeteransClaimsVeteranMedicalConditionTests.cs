using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsVeteranMedicalConditionTests
{
    [Fact]
    public void Association_PreservesVeteranAndMedicalCondition()
    {
        var veteranId =
            new VeteranId("veteran-001");

        var medicalConditionId =
            new MedicalConditionId("medical-condition-001");

        var association =
            new VeteranMedicalCondition
            {
                VeteranId = veteranId,
                MedicalConditionId = medicalConditionId
            };

        Assert.Equal(
            veteranId,
            association.VeteranId);

        Assert.Equal(
            medicalConditionId,
            association.MedicalConditionId);
    }
}
