using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEffectiveDateRegulatoryProvisionTests
{
    [Fact]
    public void Association_PreservesEffectiveDateAndRegulatoryProvision()
    {
        var effectiveDateId =
            new EffectiveDateId("effective-date-001");

        var regulatoryProvisionId =
            new RegulatoryProvisionId("provision-001");

        var association =
            new EffectiveDateRegulatoryProvision
            {
                EffectiveDateId = effectiveDateId,
                RegulatoryProvisionId =
                    regulatoryProvisionId
            };

        Assert.Equal(
            effectiveDateId,
            association.EffectiveDateId);

        Assert.Equal(
            regulatoryProvisionId,
            association.RegulatoryProvisionId);
    }
}
