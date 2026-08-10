using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsServiceConnectionBasisPresumptionTests
{
    [Fact]
    public void Association_PreservesBasisAndPresumptionProvision()
    {
        var basisId =
            new ServiceConnectionBasisId("basis-001");

        var presumptionProvisionId =
            new RegulatoryProvisionId("provision-001");

        var association =
            new ServiceConnectionBasisPresumption
            {
                ServiceConnectionBasisId = basisId,
                PresumptionProvisionId =
                    presumptionProvisionId
            };

        Assert.Equal(
            basisId,
            association.ServiceConnectionBasisId);

        Assert.Equal(
            presumptionProvisionId,
            association.PresumptionProvisionId);
    }
}
