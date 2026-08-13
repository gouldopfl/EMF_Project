using EMF.Security.Models.Identities;

namespace EMF.Security.Models;

public static class SecurityPermissions
{
    public static PermissionId ArtifactEnvelopeRewrap
    {
        get;
    } = new("artifact.envelope.rewrap");
}
