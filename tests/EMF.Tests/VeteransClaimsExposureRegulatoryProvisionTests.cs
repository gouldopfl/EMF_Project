using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsExposureRegulatoryProvisionTests
{
    [Fact]
    public void Association_PreservesExposureAndRegulatoryProvision()
    {
        var exposureId =
            new ExposureId("exposure-001");

        var provisionId =
            new RegulatoryProvisionId("provision-001");

        var association =
            new ExposureRegulatoryProvision
            {
                ExposureId = exposureId,
                RegulatoryProvisionId = provisionId
            };

        Assert.Equal(
            exposureId,
            association.ExposureId);

        Assert.Equal(
            provisionId,
            association.RegulatoryProvisionId);
    }
}
