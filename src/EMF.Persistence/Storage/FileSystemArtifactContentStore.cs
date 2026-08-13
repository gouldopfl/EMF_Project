using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Persistence.Storage;

public sealed class FileSystemArtifactContentStore :
    IArtifactContentStore
{
    private readonly string _rootPath;

    public FileSystemArtifactContentStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException(
                "Root path is required.",
                nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
    }

    public async Task WriteAsync(
        ArtifactId artifactId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_rootPath);

        var path = GetPath(artifactId);

        await File.WriteAllBytesAsync(
            path,
            content.ToArray(),
            cancellationToken);
    }

    public async Task<byte[]?> ReadAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(artifactId);

        if (!File.Exists(path))
            return null;

        return await File.ReadAllBytesAsync(
            path,
            cancellationToken);
    }

    public Task DeleteAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = GetPath(artifactId);

        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private string GetPath(ArtifactId artifactId)
    {
        var path =
            Path.GetFullPath(
                Path.Combine(_rootPath, artifactId.Value));

        var rootWithSeparator =
            _rootPath.EndsWith(
                Path.DirectorySeparatorChar)
                ? _rootPath
                : _rootPath + Path.DirectorySeparatorChar;

        if (!path.StartsWith(
                rootWithSeparator,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Artifact ID resolves outside the content store root.");
        }

        return path;
    }
}
