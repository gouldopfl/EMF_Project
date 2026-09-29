namespace EMF.Common;

public sealed class EmfVerifiedRuntimeIdentity
{
    internal EmfVerifiedRuntimeIdentity(EmfBuildManifest manifest)
    {
        Manifest = manifest;
    }

    public EmfBuildManifest Manifest { get; }

    public string BuildId => Manifest.BuildId;

    public string SourceRevisionId => Manifest.SourceRevisionId;
}
