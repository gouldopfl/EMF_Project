using EMF.Core.Models.Identities;
using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class CompositeAuthorizationPolicyTests
{
    private static AuthorizationRequest CreateRequest(
        string permission,
        string classification)
    {
        return new AuthorizationRequest
        {
            SubjectId = "user-001",
            PermissionId = new PermissionId(permission),
            ArtifactId = new ArtifactId("artifact-001"),
            ProtectionClassificationId =
                new ProtectionClassificationId(classification)
        };
    }

    private static IAuthorizationPolicy CreateAuthorizationPolicy()
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

        return new AuthorizationPolicy(provider);
    }

    [Fact]
    public async Task EvaluateAsync_BothPoliciesAllow_Allows()
    {
        var policy =
            new CompositeAuthorizationPolicy(
                CreateAuthorizationPolicy(),
                new ProtectionPolicy());

        var decision =
            await policy.EvaluateAsync(
                CreateRequest(
                    "evidence.read",
                    ProtectionClassifications.Confidential));

        Assert.Equal(
            AuthorizationDecision.Allow,
            decision);
    }

    [Fact]
    public async Task EvaluateAsync_PermissionDenied_Denies()
    {
        var policy =
            new CompositeAuthorizationPolicy(
                CreateAuthorizationPolicy(),
                new ProtectionPolicy());

        var decision =
            await policy.EvaluateAsync(
                CreateRequest(
                    "evidence.write",
                    ProtectionClassifications.Confidential));

        Assert.Equal(
            AuthorizationDecision.Deny,
            decision);
    }

    [Fact]
    public async Task EvaluateAsync_ProtectionDenied_Denies()
    {
        var policy =
            new CompositeAuthorizationPolicy(
                CreateAuthorizationPolicy(),
                new ProtectionPolicy());

        var decision =
            await policy.EvaluateAsync(
                CreateRequest(
                    "evidence.read",
                    ProtectionClassifications.Regulated));

        Assert.Equal(
            AuthorizationDecision.Deny,
            decision);
    }
}
