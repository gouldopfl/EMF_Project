using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsServiceConnectionBasisServiceConnectedConditionTests
{
    [Fact]
    public void Association_PreservesBasisAndServiceConnectedCondition()
    {
        var basisId =
            new ServiceConnectionBasisId("basis-001");

        var serviceConnectedConditionId =
            new MedicalConditionId("condition-001");

        var association =
            new ServiceConnectionBasisServiceConnectedCondition
            {
                ServiceConnectionBasisId = basisId,
                ServiceConnectedConditionId =
                    serviceConnectedConditionId
            };

        Assert.Equal(
            basisId,
            association.ServiceConnectionBasisId);

        Assert.Equal(
            serviceConnectedConditionId,
            association.ServiceConnectedConditionId);
    }
}
