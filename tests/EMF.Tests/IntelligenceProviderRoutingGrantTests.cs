using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class IntelligenceProviderRoutingGrantTests
{
    [Fact]
    public void Constructor_CapturesRoutingBoundary()
    {
        var providerId =
            new IntelligenceProviderId("provider-one");

        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var classificationId =
            new ProtectionClassificationId(
                "confidential");

        var grant =
            new IntelligenceProviderRoutingGrant(
                providerId,
                capabilityId,
                classificationId);

        Assert.Equal(providerId, grant.ProviderId);
        Assert.Equal(capabilityId, grant.CapabilityId);
        Assert.Equal(
            classificationId,
            grant.ProtectionClassificationId);
    }

    [Fact]
    public void Constructor_RejectsDefaultIdentities()
    {
        var providerId =
            new IntelligenceProviderId("provider-one");

        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var classificationId =
            new ProtectionClassificationId(
                "confidential");

        Assert.ThrowsAny<ArgumentException>(
            () => new IntelligenceProviderRoutingGrant(
                default,
                capabilityId,
                classificationId));

        Assert.ThrowsAny<ArgumentException>(
            () => new IntelligenceProviderRoutingGrant(
                providerId,
                default,
                classificationId));

        Assert.ThrowsAny<ArgumentException>(
            () => new IntelligenceProviderRoutingGrant(
                providerId,
                capabilityId,
                default));
    }
}
