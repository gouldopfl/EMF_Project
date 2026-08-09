using EMF.Core.Contracts;
using EMF.Extensions.VeteransClaims;

namespace EMF.Tests;

public sealed class VeteransClaimsDomainExtensionTests
{
    [Fact]
    public void Veterans_claims_extension_exposes_expected_platform_identity()
    {
        IDomainExtension extension = new VeteransClaimsDomainExtension();

        Assert.Equal(
            "emf.domain.veterans-claims",
            extension.ComponentId);

        Assert.Equal(
            "Veterans Claims",
            extension.DisplayName);

        Assert.Equal(
            new Version(1, 0, 0),
            extension.ComponentVersion);
    }
}
