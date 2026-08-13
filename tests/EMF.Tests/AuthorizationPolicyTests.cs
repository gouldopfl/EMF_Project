using EMF.Core.Models.Identities;
using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class AuthorizationPolicyTests
{
    private static AuthorizationRequest CreateRequest(
        string subjectId,
        string permissionId)
    {
        return new AuthorizationRequest
        {
            SubjectId = subjectId,
            PermissionId = new PermissionId(permissionId),
            ArtifactId = new ArtifactId("artifact-001"),
            ProtectionClassificationId =
                new ProtectionClassificationId("regulated")
        };
    }

    [Fact]
    public async Task EvaluateAsync_ExplicitPermission_Allows()
    {
        var context = new AuthorizationContext
        {
            SubjectId = "user-001",
            RoleIds =
            [
                new RoleId("reviewer")
            ],
            PermissionIds =
            [
                new PermissionId("evidence.read")
            ]
        };

        var provider =
            new InMemoryAuthorizationContextProvider(
                [context]);

        var policy =
            new AuthorizationPolicy(provider);

        var decision =
            await policy.EvaluateAsync(
                CreateRequest(
                    "user-001",
                    "evidence.read"));

        Assert.Equal(
            AuthorizationDecision.Allow,
            decision);
    }

    [Fact]
    public async Task EvaluateAsync_MissingPermission_Denies()
    {
        var context = new AuthorizationContext
        {
            SubjectId = "user-001",
            RoleIds =
            [
                new RoleId("reviewer")
            ],
            PermissionIds =
            [
                new PermissionId("evidence.read")
            ]
        };

        var policy =
            new AuthorizationPolicy(
                new InMemoryAuthorizationContextProvider(
                    [context]));

        var decision =
            await policy.EvaluateAsync(
                CreateRequest(
                    "user-001",
                    "evidence.write"));

        Assert.Equal(
            AuthorizationDecision.Deny,
            decision);
    }

    [Fact]
    public async Task EvaluateAsync_UnknownSubject_Denies()
    {
        var policy =
            new AuthorizationPolicy(
                new InMemoryAuthorizationContextProvider(
                    Array.Empty<AuthorizationContext>()));

        var decision =
            await policy.EvaluateAsync(
                CreateRequest(
                    "unknown",
                    "evidence.read"));

        Assert.Equal(
            AuthorizationDecision.Deny,
            decision);
    }

    [Fact]
    public async Task EvaluateAsync_EmptySubject_Denies()
    {
        var policy =
            new AuthorizationPolicy(
                new InMemoryAuthorizationContextProvider(
                    Array.Empty<AuthorizationContext>()));

        var decision =
            await policy.EvaluateAsync(
                CreateRequest(
                    "",
                    "evidence.read"));

        Assert.Equal(
            AuthorizationDecision.Deny,
            decision);
    }
}
