using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsServiceConnectionBasisClaimedConditionTests
{
    [Fact]
    public void Association_PreservesBasisAndClaimedCondition()
    {
        var basisId =
            new ServiceConnectionBasisId("basis-001");

        var claimedConditionId =
            new ClaimedConditionId("condition-001");

        var association =
            new ServiceConnectionBasisClaimedCondition
            {
                ServiceConnectionBasisId = basisId,
                ClaimedConditionId = claimedConditionId
            };

        Assert.Equal(
            basisId,
            association.ServiceConnectionBasisId);

        Assert.Equal(
            claimedConditionId,
            association.ClaimedConditionId);
    }
}
