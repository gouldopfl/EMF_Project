using EMF.Core.Models.Identities;
using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class ProtectionPolicyTests
{
    private static AuthorizationRequest CreateRequest(
        string classification)
    {
        return new AuthorizationRequest
        {
            SubjectId = "user-001",
            PermissionId =
                new PermissionId("evidence.read"),
            ResourceType = "Artifact",
            ResourceId = "artifact-001",
            ProtectionClassificationId =
                new ProtectionClassificationId(
                    classification)
        };
    }

    [Theory]
    [InlineData(ProtectionClassifications.Public)]
    [InlineData(ProtectionClassifications.Internal)]
    [InlineData(ProtectionClassifications.Confidential)]
    public async Task EvaluateAsync_BaselineClassification_Allows(
        string classification)
    {
        var policy = new ProtectionPolicy();

        var decision =
            await policy.EvaluateAsync(
                CreateRequest(classification));

        Assert.Equal(
            AuthorizationDecision.Allow,
            decision);
    }

    [Theory]
    [InlineData(ProtectionClassifications.Restricted)]
    [InlineData(ProtectionClassifications.Regulated)]
    [InlineData("Unknown")]
    public async Task EvaluateAsync_ProtectedOrUnknownClassification_Denies(
        string classification)
    {
        var policy = new ProtectionPolicy();

        var decision =
            await policy.EvaluateAsync(
                CreateRequest(classification));

        Assert.Equal(
            AuthorizationDecision.Deny,
            decision);
    }
}
