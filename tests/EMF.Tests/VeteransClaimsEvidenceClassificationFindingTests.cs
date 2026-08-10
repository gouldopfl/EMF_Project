using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceClassificationFindingTests
{
    [Fact]
    public void Association_PreservesClassificationAndFinding()
    {
        var classificationId =
            new EvidenceClassificationId("classification-001");

        var findingId =
            new FindingId("finding-001");

        var association =
            new EvidenceClassificationFinding
            {
                EvidenceClassificationId = classificationId,
                FindingId = findingId
            };

        Assert.Equal(
            classificationId,
            association.EvidenceClassificationId);

        Assert.Equal(
            findingId,
            association.FindingId);
    }
}
