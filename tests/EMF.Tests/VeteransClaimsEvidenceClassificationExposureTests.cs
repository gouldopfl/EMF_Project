using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceClassificationExposureTests
{
    [Fact]
    public void Association_PreservesClassificationAndExposure()
    {
        var classificationId =
            new EvidenceClassificationId("classification-001");

        var exposureId =
            new ExposureId("exposure-001");

        var association =
            new EvidenceClassificationExposure
            {
                EvidenceClassificationId = classificationId,
                ExposureId = exposureId
            };

        Assert.Equal(
            classificationId,
            association.EvidenceClassificationId);

        Assert.Equal(
            exposureId,
            association.ExposureId);
    }
}
