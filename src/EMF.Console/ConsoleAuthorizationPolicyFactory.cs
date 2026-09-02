using EMF.Security.Models.Identities;
using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models;

namespace EMF.ConsoleApplication;

internal static class ConsoleAuthorizationPolicyFactory
{
    public static IAuthorizationPolicy Create(
        string subjectId,
        PermissionId permissionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        var permissionPolicy =
            new AuthorizationPolicy(
                new InMemoryAuthorizationContextProvider(
                    [
                        new AuthorizationContext
                        {
                            SubjectId = subjectId,
                            RoleIds = [],
                            PermissionIds = [permissionId]
                        }
                    ]));

        return new CompositeAuthorizationPolicy(
            permissionPolicy,
            new ProtectionPolicy());
    }
}
