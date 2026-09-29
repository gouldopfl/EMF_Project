namespace EMF.Extensions.VeteransClaims.Orchestration;

/// <summary>
/// Stable renderer contract plus deterministic build identity for exact output provenance.
/// The assembly MVID changes when the compiled renderer assembly changes and is deterministic
/// for identical deterministic builds.
/// </summary>
public static class VeteransReviewerPackageRendererIdentity
{
    public const string Contract = "reviewer-docx-v1";

    public static string Build =>
        "mvid:" + typeof(VeteransReviewerPackageDocxRenderer)
            .Assembly
            .ManifestModule
            .ModuleVersionId
            .ToString("D");
}
