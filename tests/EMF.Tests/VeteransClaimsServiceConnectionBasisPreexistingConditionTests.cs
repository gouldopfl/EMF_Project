using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsServiceConnectionBasisPreexistingConditionTests
{
    [Fact]
    public void Association_PreservesBasisAndPreexistingCondition()
    {
        var basisId =
            new ServiceConnectionBasisId("basis-001");

        var preexistingConditionId =
            new MedicalConditionId("condition-001");

        var association =
            new ServiceConnectionBasisPreexistingCondition
            {
                ServiceConnectionBasisId = basisId,
                PreexistingConditionId =
                    preexistingConditionId
            };

        Assert.Equal(
            basisId,
            association.ServiceConnectionBasisId);

        Assert.Equal(
            preexistingConditionId,
            association.PreexistingConditionId);
    }
}
