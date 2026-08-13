using EMF.Security.Models;

namespace EMF.Tests;

public sealed class SecurityPermissionsTests
{
    [Fact]
    public void ArtifactEnvelopeRewrap_HasStableIdentity()
    {
        Assert.Equal(
            "artifact.envelope.rewrap",
            SecurityPermissions
                .ArtifactEnvelopeRewrap
                .Value);
    }
}
