using EMF.Common;

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
        EmfAssemblyBuildIdentity.GetModuleVersionId(
            typeof(VeteransReviewerPackageDocxRenderer).Assembly);

    public static void ValidateVerifiedDeployment(
        EmfVerifiedFirstPartyDeploymentIdentity deploymentIdentity)
    {
        ArgumentNullException.ThrowIfNull(deploymentIdentity);
        var renderer = typeof(VeteransReviewerPackageDocxRenderer).Assembly;
        deploymentIdentity.RequireVerifiedAssembly(renderer);
        var artifact = deploymentIdentity.Manifest.Artifacts.Single(candidate =>
            string.Equals(candidate.AssemblyName, renderer.GetName().Name, StringComparison.Ordinal));
        if (!string.Equals(artifact.ModuleVersionId, Build, StringComparison.Ordinal))
            throw new InvalidDataException(
                "Verified deployment renderer does not match the M91 renderer build identity.");
    }

    public static void ValidateVerifiedRuntime(
        EmfVerifiedRuntimeIdentity verifiedRuntimeIdentity)
    {
        ArgumentNullException.ThrowIfNull(verifiedRuntimeIdentity);
        EmfBuildManifestIdentity.Validate(
            verifiedRuntimeIdentity.Manifest);

        var assemblyName =
            typeof(VeteransReviewerPackageDocxRenderer)
                .Assembly
                .GetName()
                .Name;

        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            throw new InvalidDataException(
                "Reviewer renderer assembly identity is unavailable.");
        }

        var artifact =
            verifiedRuntimeIdentity.Manifest.Artifacts
                .SingleOrDefault(candidate =>
                    string.Equals(
                        candidate.AssemblyName,
                        assemblyName,
                        StringComparison.Ordinal));

        if (artifact is null)
        {
            throw new InvalidDataException(
                "Verified runtime manifest does not include the reviewer renderer assembly.");
        }

        if (!string.Equals(
                artifact.ModuleVersionId,
                Build,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Verified runtime renderer does not match the M91 renderer build identity.");
        }
    }
}
