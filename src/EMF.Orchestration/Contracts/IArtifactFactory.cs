using EMF.Core.Models.Identities;
using EMF.Discovery.Models;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IArtifactFactory
{
    ArtifactCreationResult Create(
        DiscoveredItem item,
        ArtifactId artifactId);
}
