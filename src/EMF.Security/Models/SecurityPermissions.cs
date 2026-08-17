using EMF.Security.Models.Identities;

namespace EMF.Security.Models;

public static class SecurityPermissions
{
    public static PermissionId ArtifactEnvelopeRewrap
    {
        get;
    } = new("artifact.envelope.rewrap");

    public static PermissionId ArtifactIntelligenceUse
    {
        get;
    } = new("artifact.intelligence.use");

    public static PermissionId WorkflowActivityClaimRecover
    {
        get;
    } = new("workflow.activity-claim.recover");

}
