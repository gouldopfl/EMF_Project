namespace EMF.Common;

public sealed record EmfBuildManifest(
    string SourceRevisionId,
    string Configuration,
    string TargetFramework,
    IReadOnlyList<EmfBuildArtifactIdentity> Artifacts,
    string BuildId);
