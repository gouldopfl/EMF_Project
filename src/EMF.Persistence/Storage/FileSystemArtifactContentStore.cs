using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Persistence.Storage;

public sealed class FileSystemArtifactContentStore :
    IArtifactContentStore
{
    public const long DefaultMaxStoredBytes =
        150L * 1024 * 1024;

    private readonly string _rootPath;
    private readonly long _maxStoredBytes;

    public FileSystemArtifactContentStore(
        string rootPath,
        long maxStoredBytes = DefaultMaxStoredBytes)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException(
                "Root path is required.",
                nameof(rootPath));

        if (maxStoredBytes <= 0 ||
            maxStoredBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxStoredBytes));
        }

        _rootPath = Path.GetFullPath(rootPath);
        _maxStoredBytes = maxStoredBytes;
    }

    public async Task WriteAsync(
        ArtifactId artifactId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (content.Length > _maxStoredBytes)
        {
            throw new InvalidDataException(
                "Artifact content exceeds the maximum stored size.");
        }

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

        await using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

        if (stream.Length > _maxStoredBytes)
        {
            throw new InvalidDataException(
                "Stored artifact exceeds the maximum allowed size.");
        }

        var content = new byte[(int)stream.Length];

        await stream.ReadExactlyAsync(
            content,
            cancellationToken);

        if (stream.Position != stream.Length)
        {
            throw new IOException(
                "Stored artifact changed during read.");
        }

        return content;
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
