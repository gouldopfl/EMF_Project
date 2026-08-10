using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceClassificationRequirementTests
{
    [Fact]
    public void Association_PreservesClassificationAndRequirement()
    {
        var classificationId =
            new EvidenceClassificationId("classification-001");

        var requirementId =
            new RequirementId("requirement-001");

        var association =
            new EvidenceClassificationRequirement
            {
                EvidenceClassificationId = classificationId,
                RequirementId = requirementId
            };

        Assert.Equal(
            classificationId,
            association.EvidenceClassificationId);

        Assert.Equal(
            requirementId,
            association.RequirementId);
    }
}
