using EMF.Core.Models.Identities;
using EMF.Security.Authorization;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class AuthorizationRequestTests
{
    [Fact]
    public void Request_PreservesAuthorizationContext()
    {
        var request = new AuthorizationRequest
        {
            SubjectId = "user-001",
            PermissionId =
                new PermissionId("evidence.read"),
            ArtifactId =
                new ArtifactId("artifact-001"),
            ProtectionClassificationId =
                new ProtectionClassificationId("regulated")
        };

        Assert.Equal(
            "user-001",
            request.SubjectId);

        Assert.Equal(
            "evidence.read",
            request.PermissionId.Value);

        Assert.Equal(
            "artifact-001",
            request.ArtifactId.Value);

        Assert.Equal(
            "regulated",
            request.ProtectionClassificationId.Value);
    }
}
