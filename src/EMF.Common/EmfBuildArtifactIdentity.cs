namespace EMF.Common;

public sealed record EmfBuildArtifactIdentity(
    string AssemblyName,
    string SourceRevisionId,
    string Configuration,
    string TargetFramework,
    string ModuleVersionId,
    string Sha256,
    long ByteLength);
