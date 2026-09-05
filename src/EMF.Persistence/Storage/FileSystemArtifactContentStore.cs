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
        var temporaryPath =
            $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream =
                new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan))
            {
                await stream.WriteAsync(
                    content,
                    cancellationToken);
            }

            File.Move(
                temporaryPath,
                path,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
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
        var value = artifactId.Value;

        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            value.Contains('/') ||
            value.Contains('\\') ||
            Path.IsPathRooted(value))
        {
            throw new InvalidOperationException(
                "Artifact ID must be a single file name.");
        }

        var path =
            Path.GetFullPath(
                Path.Combine(_rootPath, value));

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
