using EMF.Discovery.Models;

namespace EMF.Discovery.Contracts;

public interface IArtifactInspectionService
{
    Task<ArtifactInspectionResult> InspectAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
