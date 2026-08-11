using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsExposureRequirementTests
{
    [Fact]
    public void Association_PreservesExposureAndRequirement()
    {
        var exposureId =
            new ExposureId("exposure-001");

        var requirementId =
            new RequirementId("requirement-001");

        var association =
            new ExposureRequirement
            {
                ExposureId = exposureId,
                RequirementId = requirementId
            };

        Assert.Equal(
            exposureId,
            association.ExposureId);

        Assert.Equal(
            requirementId,
            association.RequirementId);
    }
}
