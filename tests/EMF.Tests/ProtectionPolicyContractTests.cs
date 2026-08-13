using EMF.Security.Authorization;

namespace EMF.Tests;

public sealed class ProtectionPolicyContractTests
{
    [Fact]
    public void ProtectionPolicy_UsesAuthorizationDecision()
    {
        Assert.Equal(
            AuthorizationDecision.Deny,
            (AuthorizationDecision)0);

        Assert.Equal(
            AuthorizationDecision.Allow,
            (AuthorizationDecision)1);
    }
}
