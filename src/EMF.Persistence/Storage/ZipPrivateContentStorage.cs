using System.Security.Cryptography;
using System.Text;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;

namespace EMF.Persistence.Storage;

// Owns a dedicated ZIP-only root. Canonical artifact APIs are deliberately absent.
public sealed class ZipPrivateContentStorage : IZipPrivateContentStorage
{
    private readonly FileSystemArtifactContentStore _physical;
    private readonly FileSystemArtifactContentStagingStore _staging;
    private readonly IContentStoragePlatform _platform=ContentStoragePlatform.Select();
    private readonly string _stagingRoot;
    public string NamespaceId { get; }
    public long MaximumProtectedBytes { get; }
    public ZipPrivateContentStorage(string privateRoot,long maximumProtectedBytes)
    {
        var root=Path.GetFullPath(privateRoot);
        if(maximumProtectedBytes<=0||maximumProtectedBytes>Array.MaxLength)throw new ArgumentOutOfRangeException(nameof(maximumProtectedBytes));
        MaximumProtectedBytes=maximumProtectedBytes;
        _stagingRoot=Path.Combine(root,"zip-candidates");
        _staging=new(_stagingRoot,checked((int)maximumProtectedBytes));
        _physical=new(Path.Combine(root,"zip-quarantine"),maximumProtectedBytes);
        NamespaceId=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)));
    }
    private string Candidate(ArtifactContentOperationId op)
    {
        ArtifactContentIdentity.Validate(op.Value);
        return Path.Combine(_stagingRoot,Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(op.Value)))+".candidate");
    }
    private static void Id(ArtifactId id)
    { if(!id.Value.StartsWith("zip-quarantine-",StringComparison.Ordinal))throw new InvalidDataException("ZIP private object identity required."); }
    public Task StageAsync(ArtifactContentOperationId op,ReadOnlyMemory<byte> candidate,CancellationToken ct=default)=>_staging.StageAsync(op,candidate,ct);
    public async Task<byte[]?> ReadCandidateAsync(ArtifactContentOperationId op,CancellationToken ct=default)
    {
        var path=Candidate(op);if(!File.Exists(path))return null;
        if(new FileInfo(path).LinkTarget is not null)throw new IOException("ZIP candidate is a symbolic link.");
        _platform.ValidatePrivatePermissions(path);
        await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous);
        if(file.Length>MaximumProtectedBytes)throw new InvalidDataException("ZIP candidate exceeds its preallocation ceiling.");
        var bytes=new byte[checked((int)file.Length)];
        try { await file.ReadExactlyAsync(bytes,ct);return bytes; }
        catch { CryptographicOperations.ZeroMemory(bytes);throw; }
    }
    public Task<ArtifactContentMutationReceipt?> GetReceiptAsync(ArtifactContentOperationId op,CancellationToken ct=default)=>_physical.GetMutationOutcomeAsync(op,ct);
    public Task<IPreparedArtifactContentMutation> PrepareCreateAsync(ArtifactId id,ReadOnlyMemory<byte> candidate,ArtifactContentMutationContext context,CancellationToken ct=default)
    { Id(id);return _physical.PreparePhysicalCreateAsync(id,candidate,context,ct); }
    public Task<IPreparedArtifactContentMutation> PrepareDeleteAsync(ArtifactId id,ArtifactContentRevision revision,ArtifactContentMutationContext context,CancellationToken ct=default)
    { Id(id);return _physical.PreparePhysicalDeleteAsync(id,revision,context,ct); }
    public async Task<IArtifactContentReadLease?> ReadProtectedAsync(ArtifactId id,ArtifactContentRevision expected,CancellationToken ct=default)
    {
        Id(id);
        try{return await _physical.ReadBoundedVersionedAsync(id,new(MaximumProtectedBytes,MaximumProtectedBytes,expected),ct);}
        catch(InvalidOperationException error){throw new InvalidDataException("ZIP retained revision evidence is contradictory.",error);}
    }
    public async Task<IZipPreparedCandidateCleanup> PrepareCandidateCleanupAsync(ArtifactContentOperationId op,string hash,CancellationToken ct=default)
    {
        var gate=await _platform.AcquireAsync(Path.Combine(_stagingRoot,".staging-gate"),true,ct);
        try
        {
            var bytes=await ReadCandidateAsync(op,ct);
            try
            {
                if(bytes is not null && Convert.ToHexString(SHA256.HashData(bytes))!=hash)throw new InvalidDataException("Frozen ZIP candidate changed before cleanup.");
                return new Cleanup(this,op,hash,gate,bytes is null);
            }
            finally{if(bytes is not null)CryptographicOperations.ZeroMemory(bytes);}
        }
        catch{gate.Dispose();throw;}
    }
    private sealed class Cleanup(ZipPrivateContentStorage store,ArtifactContentOperationId op,string hash,IDisposable gate,bool absent) : IZipPreparedCandidateCleanup
    {
        private bool _disposed,_executed;
        public Task<ZipCandidateCleanupReceipt> ExecuteAsync(CancellationToken ct=default)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);ct.ThrowIfCancellationRequested();
            if(_executed)throw new InvalidOperationException("ZIP candidate cleanup is single-use.");_executed=true;
            var path=store.Candidate(op);
            if(!absent)
            {
                if(new FileInfo(path).LinkTarget is not null)throw new IOException("Candidate substituted before cleanup.");
                File.Delete(path);store._platform.FlushDirectory(store._stagingRoot);
            }
            return Task.FromResult(new ZipCandidateCleanupReceipt(op.Value,hash,absent));
        }
        public ValueTask DisposeAsync(){if(!_disposed){_disposed=true;gate.Dispose();}return ValueTask.CompletedTask;}
    }
}
