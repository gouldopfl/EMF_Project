using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
namespace EMF.Persistence.Storage;

public sealed class FileSystemArtifactContentStagingStore : IArtifactContentStagingStore
{
    private readonly string _root;
    private readonly IContentStoragePlatform _platform;
    private readonly int _maximumBytes;
    public FileSystemArtifactContentStagingStore(string root, int maximumBytes = 150 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _root = Path.GetFullPath(root); _maximumBytes = maximumBytes; _platform = ContentStoragePlatform.Select();
        _platform.RequirePlatform();
        // Reject symlink ancestors; the private root must belong to the service principal.
        for (var path = _root; path is not null; path = Path.GetDirectoryName(path))
            if (new DirectoryInfo(path).LinkTarget is not null) throw new IOException("Staging path contains a symbolic link.");
        _platform.CreatePrivateDirectory(_root); _platform.ValidatePrivatePermissions(_root);
        _platform.FlushDirectory(_root, verifyFileSystem: true);
        _platform.FlushDirectory(Path.GetDirectoryName(_root)!);
    }
    private string Candidate(ArtifactContentOperationId operationId)
    {
        ArtifactContentIdentity.Validate(operationId.Value);
        return Path.Combine(_root, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(operationId.Value))) + ".candidate");
    }
    public async Task StageAsync(ArtifactContentOperationId operationId, ReadOnlyMemory<byte> encryptedCandidate, CancellationToken cancellationToken = default)
    {
        if (encryptedCandidate.Length > _maximumBytes) throw new ArgumentException("Candidate exceeds staging bound.");
        var target = Candidate(operationId);
        using var gate = await _platform.AcquireAsync(Path.Combine(_root, ".staging-gate"), true, cancellationToken);
        if (File.Exists(target))
        {
            var existing = await ReadAsync(operationId, cancellationToken);
            if (!existing!.AsSpan().SequenceEqual(encryptedCandidate.Span)) throw new ArtifactContentIdempotencyException();
            return;
        }
        var temporary = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".preparing");
        try
        {
            await using (var stream = _platform.CreatePrivateFile(temporary, asynchronous: true))
            {
                await stream.WriteAsync(encryptedCandidate, cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target); _platform.FlushDirectory(_root);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); _platform.FlushDirectory(_root); } }
    }
    public async Task<byte[]?> ReadAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default)
    {
        var path = Candidate(operationId);
        if (!File.Exists(path)) return null;
        _platform.ValidatePrivatePermissions(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (stream.Length > _maximumBytes) throw new InvalidDataException("Staged candidate exceeds its bound.");
        var bytes = new byte[(int)stream.Length]; await stream.ReadExactlyAsync(bytes, cancellationToken); return bytes;
    }
}
