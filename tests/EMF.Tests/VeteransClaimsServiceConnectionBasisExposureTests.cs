using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsServiceConnectionBasisExposureTests
{
    [Fact]
    public void Association_PreservesBasisAndExposure()
    {
        var basisId =
            new ServiceConnectionBasisId("basis-001");

        var exposureId =
            new ExposureId("exposure-001");

        var association =
            new ServiceConnectionBasisExposure
            {
                ServiceConnectionBasisId = basisId,
                ExposureId = exposureId
            };

        Assert.Equal(
            basisId,
            association.ServiceConnectionBasisId);

        Assert.Equal(
            exposureId,
            association.ExposureId);
    }
}
