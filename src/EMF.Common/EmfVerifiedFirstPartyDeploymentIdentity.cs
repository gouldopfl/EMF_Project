using System.Reflection;

namespace EMF.Common;

/// <summary>
/// The complete first-party deployment file inventory matched an externally supplied
/// expectation. This is not an attestation of CLR-loaded PE bytes. The deployment,
/// expectation, and path indirections must remain immutable from before process
/// startup through generation; source authenticity and other components are outside
/// this boundary.
/// </summary>
public sealed class EmfVerifiedFirstPartyDeploymentIdentity
{
    private readonly Assembly[] _assemblies;

    internal EmfVerifiedFirstPartyDeploymentIdentity(
        EmfBuildManifest manifest,
        IEnumerable<Assembly> assemblies)
    {
        Manifest = manifest with
        {
            Artifacts = Array.AsReadOnly(manifest.Artifacts.ToArray())
        };
        _assemblies = assemblies.ToArray();
    }

    public EmfBuildManifest Manifest { get; }
    public string BuildId => Manifest.BuildId;
    public string SourceRevisionId => Manifest.SourceRevisionId;

    public void RequireVerifiedAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (!_assemblies.Any(candidate => ReferenceEquals(candidate, assembly)))
        {
            throw new InvalidDataException(
                "Deployment verification does not include the required assembly instance.");
        }
    }
}
