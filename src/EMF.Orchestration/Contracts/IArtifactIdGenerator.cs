using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Contracts;

public interface IArtifactIdGenerator
{
    ArtifactId Generate();
}
