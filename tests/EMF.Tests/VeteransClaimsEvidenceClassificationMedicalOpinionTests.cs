using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceClassificationMedicalOpinionTests
{
    [Fact]
    public void Association_PreservesClassificationAndMedicalOpinion()
    {
        var classificationId =
            new EvidenceClassificationId("classification-001");

        var medicalOpinionId =
            new MedicalOpinionId("medical-opinion-001");

        var association =
            new EvidenceClassificationMedicalOpinion
            {
                EvidenceClassificationId = classificationId,
                MedicalOpinionId = medicalOpinionId
            };

        Assert.Equal(
            classificationId,
            association.EvidenceClassificationId);

        Assert.Equal(
            medicalOpinionId,
            association.MedicalOpinionId);
    }
}
