using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class EvidenceDevelopmentResultTests
{
    [Fact]
    public void Result_PreservesGapRequirementAndGuidance()
    {
        var result = new EvidenceDevelopmentResult
        {
            EvidenceGapId = new EvidenceGapId("gap-1"),
            RequirementId = new RequirementId("req-1"),
            EvidenceGuidance = Array.Empty<EvidenceRequirementGuidance>()
        };

        Assert.Equal("gap-1", result.EvidenceGapId.Value);
        Assert.Equal("req-1", result.RequirementId.Value);
        Assert.Empty(result.EvidenceGuidance);
    }
}
