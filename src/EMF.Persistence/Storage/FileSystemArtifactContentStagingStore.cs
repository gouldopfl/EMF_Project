using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
namespace EMF.Persistence.Storage;

public sealed class FileSystemArtifactContentStagingStore : IArtifactContentStagingStore, IZipGuardedArtifactContentStagingStore
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
    internal Func<string, Task>? GuardedCheckpoint { get; set; }
    private async Task GuardedAt(string point) { if (GuardedCheckpoint is not null) await GuardedCheckpoint(point); }
    public async Task StageGuardedAsync(ArtifactContentOperationId operation, ReadOnlyMemory<byte> candidate,
        IZipEffectBoundaryGuard guard, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, guard.Invalidated);
        ct = linked.Token;
        await guard.RevalidateAsync(ct);
        if (candidate.Length > _maximumBytes) throw new ArgumentException("Candidate exceeds staging bound.");
        var target = Candidate(operation);
        using var gate = await _platform.AcquireAsync(Path.Combine(_root, ".staging-gate"), true, ct);
        await guard.RevalidateAsync(ct);
        if (File.Exists(target))
        {
            var existing = await ReadGuardedAsync(operation, guard, ct);
            try
            {
                await guard.RevalidateAsync(ct);
                using (AcceptGuard(guard, ct))
                    if (existing is null || !existing.AsSpan().SequenceEqual(candidate.Span)) throw new ArtifactContentIdempotencyException();
            }
            finally { if (existing is not null) CryptographicOperations.ZeroMemory(existing); }
            return;
        }
        var temporary = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".preparing");
        try
        {
            await GuardedAt("BeforeCreate");
            await guard.RevalidateAsync(ct);
            var created = OpenAccepted(() => _platform.CreatePrivateFile(temporary, asynchronous: true), guard, ct);
            await using (var stream = created)
            {
                await guard.RevalidateAsync(ct);
                await CompleteAcceptedAsync(() => stream.WriteAsync(candidate, ct), guard, ct);
                await guard.RevalidateAsync(ct);
                using (AcceptGuard(guard, ct)) stream.Flush(flushToDisk: true);
            }
            await GuardedAt("BeforeRename");
            await guard.RevalidateAsync(ct);
            using (AcceptGuard(guard, ct)) { File.Move(temporary, target); _platform.FlushDirectory(_root); }
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); _platform.FlushDirectory(_root); } }
    }
    public async Task<byte[]?> ReadGuardedAsync(ArtifactContentOperationId operation, IZipEffectBoundaryGuard guard, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, guard.Invalidated);
        ct = linked.Token;
        await guard.RevalidateAsync(ct);
        var path = Candidate(operation);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).LinkTarget is not null) throw new IOException("Staged candidate is a symbolic link.");
        _platform.ValidatePrivatePermissions(path);
        var opened = OpenAccepted(() => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous), guard, ct);
        await using var stream = opened;
        if (stream.Length > _maximumBytes) throw new InvalidDataException("Staged candidate exceeds its bound.");
        byte[] bytes = [];
        try
        {
            using (AcceptGuard(guard, ct)) bytes = new byte[checked((int)stream.Length)];
            await CompleteAcceptedAsync(() => stream.ReadExactlyAsync(bytes, ct), guard, ct);
            await stream.DisposeAsync();
            await guard.RevalidateAsync(ct);
            return bytes;
        }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }
    // Started I/O owns its buffers until completion, even if acceptance release fails.
    internal static async ValueTask CompleteAcceptedAsync(Func<ValueTask> start, IZipEffectBoundaryGuard guard, CancellationToken ct)
    {
        ValueTask pending = default;
        Exception? releaseFailure = null;
        var started = false;
        try { using (AcceptGuard(guard, ct)) { pending = start(); started = true; } }
        catch (Exception error) { if (!started) throw; releaseFailure = error; }
        try { await pending; }
        catch when (releaseFailure is not null) { /* Preserve the acceptance failure after quiescence. */ }
        if (releaseFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(releaseFailure).Throw();
    }
    private static FileStream OpenAccepted(Func<FileStream> open, IZipEffectBoundaryGuard guard, CancellationToken ct)
    {
        FileStream? stream = null;
        try { using (AcceptGuard(guard, ct)) stream = open(); return stream; }
        catch { stream?.Dispose(); throw; }
    }
    private static IDisposable AcceptGuard(IZipEffectBoundaryGuard guard, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return guard.Accept(ct) ?? throw new UnauthorizedAccessException("ZIP effect acceptance is missing.");
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
