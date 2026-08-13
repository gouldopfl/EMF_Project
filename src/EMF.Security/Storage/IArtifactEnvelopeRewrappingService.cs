using EMF.Security.Storage.Models;

namespace EMF.Security.Storage;

public interface IArtifactEnvelopeRewrappingService
{
    Task<ArtifactEnvelopeRewrappingResult> RewrapAsync(
        ArtifactEnvelopeRewrappingRequest request,
        CancellationToken cancellationToken = default);
}
