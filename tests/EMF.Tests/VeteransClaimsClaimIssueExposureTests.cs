using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsClaimIssueExposureTests
{
    [Fact]
    public void Association_PreservesClaimIssueAndExposure()
    {
        var claimIssueId =
            new ClaimIssueId("claim-issue-001");

        var exposureId =
            new ExposureId("exposure-001");

        var association =
            new ClaimIssueExposure
            {
                ClaimIssueId = claimIssueId,
                ExposureId = exposureId
            };

        Assert.Equal(
            claimIssueId,
            association.ClaimIssueId);

        Assert.Equal(
            exposureId,
            association.ExposureId);
    }
}
