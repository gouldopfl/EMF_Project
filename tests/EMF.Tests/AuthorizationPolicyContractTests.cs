using EMF.Security.Authorization;

namespace EMF.Tests;

public sealed class AuthorizationPolicyContractTests
{
    [Fact]
    public void AuthorizationDecision_DefinesAllowAndDeny()
    {
        Assert.Equal(
            AuthorizationDecision.Deny,
            (AuthorizationDecision)0);

        Assert.Equal(
            AuthorizationDecision.Allow,
            (AuthorizationDecision)1);
    }
}
