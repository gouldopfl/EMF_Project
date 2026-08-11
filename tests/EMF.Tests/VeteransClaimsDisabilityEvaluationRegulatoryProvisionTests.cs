using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsDisabilityEvaluationRegulatoryProvisionTests
{
    [Fact]
    public void Association_PreservesEvaluationAndRegulatoryProvision()
    {
        var disabilityEvaluationId =
            new DisabilityEvaluationId("evaluation-001");

        var regulatoryProvisionId =
            new RegulatoryProvisionId("provision-001");

        var association =
            new DisabilityEvaluationRegulatoryProvision
            {
                DisabilityEvaluationId =
                    disabilityEvaluationId,
                RegulatoryProvisionId =
                    regulatoryProvisionId
            };

        Assert.Equal(
            disabilityEvaluationId,
            association.DisabilityEvaluationId);

        Assert.Equal(
            regulatoryProvisionId,
            association.RegulatoryProvisionId);
    }
}
