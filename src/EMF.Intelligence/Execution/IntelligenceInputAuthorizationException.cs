using EMF.Core.Models.Identities;

namespace EMF.Intelligence.Execution;

public sealed class
    IntelligenceInputAuthorizationException :
    UnauthorizedAccessException
{
    public IntelligenceInputAuthorizationException(
        ArtifactId artifactId)
        : base(
            "Use of the input Artifact for an " +
            "intelligence operation was denied.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            artifactId.Value);

        ArtifactId = artifactId;
    }

    public ArtifactId ArtifactId { get; }
}
