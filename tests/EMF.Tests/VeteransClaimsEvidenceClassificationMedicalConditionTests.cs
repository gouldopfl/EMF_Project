using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceClassificationMedicalConditionTests
{
    [Fact]
    public void Association_PreservesClassificationAndMedicalCondition()
    {
        var classificationId =
            new EvidenceClassificationId("classification-001");

        var medicalConditionId =
            new MedicalConditionId("medical-condition-001");

        var association =
            new EvidenceClassificationMedicalCondition
            {
                EvidenceClassificationId = classificationId,
                MedicalConditionId = medicalConditionId
            };

        Assert.Equal(
            classificationId,
            association.EvidenceClassificationId);

        Assert.Equal(
            medicalConditionId,
            association.MedicalConditionId);
    }
}
