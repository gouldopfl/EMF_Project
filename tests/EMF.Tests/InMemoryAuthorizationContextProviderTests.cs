using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class InMemoryAuthorizationContextProviderTests
{
    [Fact]
    public async Task GetContextAsync_ReturnsMatchingContext()
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

        var result =
            await provider.GetContextAsync("user-001");

        Assert.NotNull(result);
        Assert.Equal("user-001", result.SubjectId);
        Assert.Single(result.RoleIds);
        Assert.Single(result.PermissionIds);
    }

    [Fact]
    public async Task GetContextAsync_UnknownSubject_ReturnsNull()
    {
        var provider =
            new InMemoryAuthorizationContextProvider(
                Array.Empty<AuthorizationContext>());

        var result =
            await provider.GetContextAsync("unknown");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetContextAsync_EmptySubject_ReturnsNull()
    {
        var provider =
            new InMemoryAuthorizationContextProvider(
                Array.Empty<AuthorizationContext>());

        var result =
            await provider.GetContextAsync("");

        Assert.Null(result);
    }
}
