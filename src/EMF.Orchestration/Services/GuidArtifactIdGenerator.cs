using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed class GuidArtifactIdGenerator : IArtifactIdGenerator
{
    public ArtifactId Generate()
    {
        return new ArtifactId(
            Guid.NewGuid().ToString("N"));
    }
}
