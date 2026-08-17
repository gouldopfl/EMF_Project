using EMF.Security.Models;
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
            ResourceType = "Artifact",
            ResourceId = "artifact-001",
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
            SecurityResourceTypes.Artifact,
            request.ResourceType);

        Assert.Equal(
            "artifact-001",
            request.ResourceId);

        Assert.Equal(
            "regulated",
            request.ProtectionClassificationId.Value);
    }

    [Fact]
    public void Request_preserves_workflow_identity()
    {
        var request = new AuthorizationRequest
        {
            SubjectId = "steward",
            PermissionId = new("workflow.claim.recover"),
            ResourceType = SecurityResourceTypes.Workflow,
            ResourceId = "workflow-001",
            ProtectionClassificationId = new("internal")
        };

        Assert.Equal("Workflow", request.ResourceType);
        Assert.Equal("workflow-001", request.ResourceId);
    }
}
