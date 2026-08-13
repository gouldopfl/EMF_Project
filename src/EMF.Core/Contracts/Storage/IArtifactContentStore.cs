using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts.Storage;

public interface IArtifactContentStore
{
    Task WriteAsync(
        ArtifactId artifactId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);

    Task<byte[]?> ReadAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default);
}
