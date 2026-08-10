using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsLegalAnalysisTraceabilityTests
{
    [Fact]
    public void LegalAnalysisRegulatoryProvision_PreservesAssociation()
    {
        var analysisId = new LegalAnalysisId("analysis-001");
        var provisionId = new RegulatoryProvisionId("provision-001");

        var reference = new LegalAnalysisRegulatoryProvision
        {
            LegalAnalysisId = analysisId,
            RegulatoryProvisionId = provisionId
        };

        Assert.Equal(analysisId, reference.LegalAnalysisId);
        Assert.Equal(provisionId, reference.RegulatoryProvisionId);
    }
}
