using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Storage;

namespace EMF.Orchestration.Services;

public sealed class ZipProtectedRetentionService
{
    private readonly IZipExtractionJournal _parents;
    private readonly IZipRetentionJournal _journal;
    private readonly IZipPrivateContentStorage _storage;
    private readonly IEnvelopeEncryptionService _encryption;
    private readonly IBoundedEnvelopeDecryptionService _decryption;
    private readonly EnvelopeDecryptionLimits _limits=new(ZipNumericLimits.Child);
    internal Func<string,Task>? Checkpoint { get; set; }
    public ZipProtectedRetentionService(IZipExtractionJournal parents,IZipRetentionJournal journal,
        IZipPrivateContentStorage storage,IEnvelopeEncryptionService encryption)
    {
        (_parents,_journal,_storage,_encryption)=(parents,journal,storage,encryption);
        _decryption=encryption as IBoundedEnvelopeDecryptionService??throw new NotSupportedException("ZIP retention requires bounded context decryption.");
        if(storage.MaximumProtectedBytes!=BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Child))
            throw new InvalidOperationException("ZIP private namespace must enforce the closed protected-child ceiling.");
    }
    private Task At(string name)=>Checkpoint?.Invoke(name)??Task.CompletedTask;
    private static string Hash(ReadOnlySpan<byte> bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static ArtifactId Id(ZipRetentionRecord r)=>new(r.Identity.ObjectId);
    private static ArtifactContentMutationContext Context(ZipRetentionRecord r,bool release)=>new(new(release?r.Identity.ReleaseOperationId:r.Identity.CreateOperationId),new ArtifactContentOwnershipToken(r.Identity.OwnerToken));
    private void Namespace(ZipRetentionRecord r)
    { if(r.Identity.NamespaceId!=_storage.NamespaceId)throw new InvalidDataException("ZIP private namespace binding changed."); }
    private async Task<ZipRetentionRecord?> ReadRetentionAsync(ZipParentSnapshot parent,int fileOrdinal,bool required,CancellationToken ct,bool governed=false)
    {
        try
        {
            var record=await _journal.ReadRetentionAsync(parent.Binding.OperationId,fileOrdinal,ct);
            if(required&&record is null)throw new InvalidDataException("Missing durable ZIP retention evidence.");
            return record;
        }
        catch(Exception error) when((error is InvalidDataException or IOException or System.Text.Json.JsonException)&&!governed)
        {
            using var review=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);
            await _parents.ReviewAsync(parent.Fence,"RetentionJournalEvidenceFailure",review.Token);throw;
        }
    }
    private async Task ValidateCandidateAsync(ZipRetentionRecord r,ReadOnlyMemory<byte> candidate,CancellationToken ct,IZipEffectBoundaryGuard? guard=null)
    {
        await ZipPhaseIIWork.CheckAsync(guard,ct);
        if(candidate.Length>_storage.MaximumProtectedBytes||(r.CandidateHash is not null&&ZipPhaseIIWork.Hash(candidate.Span,guard,ct)!=r.CandidateHash))
            throw new InvalidDataException("ZIP frozen protected candidate changed.");
        EncryptedEnvelope? envelope=null;byte[]? plain=null;
        try
        {
            envelope=ZipPhaseIIWork.Decode(candidate.Span,_limits,guard,ct);
            plain=await ZipPhaseIIWork.Decrypt(_decryption,envelope,ArtifactEnvelopeContext.Create(Id(r)),_limits,guard,ct);
            await ZipPhaseIIWork.CheckAsync(guard,ct);
            if(plain.LongLength!=r.Identity.Length||ZipPhaseIIWork.Hash(plain,guard,ct)!=r.Identity.PlaintextSha256)throw new InvalidDataException("ZIP retained plaintext binding changed.");
        }
        finally{if(plain is not null)CryptographicOperations.ZeroMemory(plain);if(envelope is not null)BoundedEncryptedEnvelopeCodec.Clear(envelope);}
    }
    internal Task<ZipRetentionUpdate> SealAsync(ZipParentSnapshot parent,int fileOrdinal,ReadOnlyMemory<byte>? plaintext,CancellationToken ct=default)
        => SealCoreAsync(parent,fileOrdinal,plaintext,null,ct);
    public Task<ZipRetentionUpdate> SealAsync(ZipParentSnapshot parent,int fileOrdinal,ReadOnlyMemory<byte>? plaintext,ZipExecutionAttempt attempt,CancellationToken ct=default)
        => SealCoreAsync(parent,fileOrdinal,plaintext,attempt ?? throw new ArgumentNullException(nameof(attempt)),ct);
    private async Task<ZipRetentionUpdate> SealCoreAsync(ZipParentSnapshot parent,int fileOrdinal,ReadOnlyMemory<byte>? plaintext,ZipExecutionAttempt? attempt,CancellationToken ct)
    {
        ZipRetentionRecord? retention = null;
        await using var guard = attempt?.RetentionBoundary(() => parent,fileOrdinal,() => retention,_storage.NamespaceId);
        using var governed = guard is null ? CancellationTokenSource.CreateLinkedTokenSource(ct) : CancellationTokenSource.CreateLinkedTokenSource(ct,guard.Invalidated);
        ct = governed.Token;
        await ZipPhaseIIWork.CheckAsync(guard,ct);
        if(attempt is not null&&_storage.NamespaceId!=attempt.Admission.Binding.ChildNamespaceId)throw new UnauthorizedAccessException("Child namespace differs from admission.");
        ct.ThrowIfCancellationRequested();
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(ZipNumericLimits.AttemptTimeout);ct=budget.Token;
        var fenceCheck=await _parents.ReadAsync(parent.Binding.OperationId,ct);if(fenceCheck?.Fence!=parent.Fence)throw new ZipFenceException();
        retention=await ReadRetentionAsync(parent,fileOrdinal,false,ct,attempt is not null);
        await ZipPhaseIIWork.CheckAsync(guard,ct);
        if(retention is null)
        {
            if(plaintext is null)throw new InvalidDataException("ZIP child has no durable preparation identity.");
            var entry=parent.Entries.Single(e=>e.Plan.FileOrdinal==fileOrdinal);
            if(plaintext.Value.Length!=entry.Plan.ExpandedLength||plaintext.Value.Length>ZipNumericLimits.Child)throw new InvalidDataException("ZIP child length exceeds binding.");
            await ZipPhaseIIWork.CheckAsync(guard,ct);
            string plaintextHash;
            plaintextHash=ZipPhaseIIWork.Hash(plaintext.Value.Span,guard,ct);
            var reserved=guard is null ? await _journal.ReserveRetentionAsync(parent.Fence,fileOrdinal,_storage.NamespaceId,plaintextHash,ct)
                : await _journal.ReserveRetentionAsync(parent.Fence,fileOrdinal,_storage.NamespaceId,plaintextHash,guard,ct);
            parent=reserved.Parent;retention=reserved.Retention;await At("Reserved");
        }
        byte[]? candidate=null;EncryptedEnvelope? envelope=null;
        try
        {
            await ZipPhaseIIWork.CheckAsync(guard,ct);
            Namespace(retention);
            if(plaintext is not null&&(plaintext.Value.Length!=retention.Identity.Length||ZipPhaseIIWork.Hash(plaintext.Value.Span,guard,ct)!=retention.Identity.PlaintextSha256))
                throw new InvalidDataException("ZIP retained request changed.");
            if(retention.State==ZipRetentionState.Created)
            {
                var verified=await OpenCoreAsync(parent,fileOrdinal,guard,ct);
                await verified.DisposeAsync();
                if(guard is not null) await guard.FinishAsync(ct);
                return new(parent,retention);
            }
            if(retention.State is ZipRetentionState.ReleasePending or ZipRetentionState.Released or ZipRetentionState.RequiresReview)
                throw new InvalidOperationException("ZIP retention is not writable.");
            var mayEncrypt=false;
            if(retention.State==ZipRetentionState.Reserved)
            {
                if(plaintext is null)throw new InvalidDataException("Unstarted retention needs its admitted plaintext.");
                var started=guard is null ? await _journal.BeginRetentionPreparationAsync(parent.Fence,retention,ct) : await _journal.BeginRetentionPreparationAsync(parent.Fence,retention,guard,ct);
                parent=started.Parent;retention=started.Retention;mayEncrypt=true;await At("PreparationStarted");
            }
            var known=await _storage.GetReceiptAsync(new(retention.Identity.CreateOperationId),ct);
            candidate=await ZipPhaseIIWork.Candidate(_storage,new(retention.Identity.CreateOperationId),guard,ct);
            if(candidate is null&&mayEncrypt&&known is null)
            {
                envelope=await ZipPhaseIIWork.Encrypt(_encryption,plaintext!.Value,ArtifactEnvelopeContext.Create(Id(retention)),guard,ct);
                candidate=ZipPhaseIIWork.Encode(envelope,_limits,guard,ct);
                await ZipPhaseIIWork.Stage(_storage,new(retention.Identity.CreateOperationId),candidate,guard,ct);await At("Staged");
            }
            if(candidate is null)
            {
                if(known is null||retention.CandidateHash is null)throw new InvalidDataException("ZIP bound randomized candidate is missing; regeneration forbidden.");
                using var raw=await ZipPhaseIIWork.Read(_storage,Id(retention),known.CurrentRevision??throw new InvalidDataException(),guard,ct)
                    ??throw new InvalidDataException("ZIP receipt has no exact retained content.");
                if(ZipPhaseIIWork.Hash(raw.Content.Span,guard,ct)!=retention.CandidateHash)throw new InvalidDataException("ZIP physical candidate hash changed.");
                await ValidateCandidateAsync(retention,raw.Content,ct,guard);
            }
            else await ValidateCandidateAsync(retention,candidate,ct,guard);
            if(retention.State==ZipRetentionState.Preparing)
            {
                if(known is not null)throw new InvalidDataException("Unbound candidate has unexpected physical receipt.");
                var frozen=guard is null ? await _journal.BindRetentionCandidateAsync(parent.Fence,retention,ZipPhaseIIWork.Hash(candidate!,guard,ct),ct) : await _journal.BindRetentionCandidateAsync(parent.Fence,retention,ZipPhaseIIWork.Hash(candidate!,guard,ct),guard,ct);
                parent=frozen.Parent;retention=frozen.Retention;await At("CandidateBound");
            }
            if(known is not null)
            {
                using var raw=await ZipPhaseIIWork.Read(_storage,Id(retention),known.CurrentRevision??throw new InvalidDataException(),guard,ct)
                    ??throw new InvalidDataException("Missing exact created revision.");
                if(ZipPhaseIIWork.Hash(raw.Content.Span,guard,ct)!=retention.CandidateHash)throw new InvalidDataException("Created candidate differs from frozen binding.");
            }
            ZipRetentionUpdate created;
            await using(var physical=known is null?await ZipPhaseIIWork.Prepare(_storage,Id(retention),candidate!,Context(retention,false),guard,ct):null)
            {
                await using IPreparedArtifactContentMutation? prepared=physical is null?null:guard is null ? new CheckpointMutation(physical,()=>At("PhysicalCreated"))
                    : new GuardedCheckpointMutation(physical,guard,()=>At("PhysicalCreated"));
                created=guard is null ? await _journal.PromoteRetentionAsync(parent.Fence,retention,prepared,known,ct)
                    : await _journal.PromoteRetentionAsync(parent.Fence,retention,prepared,known,guard,ct);
                parent=created.Parent;retention=created.Retention;
                await At("Created");
            }
            if(guard is not null) await guard.FinishAsync(ct);
            return created;
        }
        catch(Exception error) when((error is InvalidDataException or IOException or CryptographicException)&&attempt is null)
        {
            using var cleanup=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);
            var currentRetention=await _journal.ReadRetentionAsync(parent.Binding.OperationId,fileOrdinal,cleanup.Token);
            if(currentRetention?.State!=ZipRetentionState.RequiresReview)
                await _journal.ReviewRetentionAsync(parent.Fence,retention,"RetentionEvidenceFailure",cleanup.Token);
            throw;
        }
        finally{if(candidate is not null)CryptographicOperations.ZeroMemory(candidate);if(envelope is not null)BoundedEncryptedEnvelopeCodec.Clear(envelope);}
    }
    internal void ValidateScanDependencies()
    {
        _=ZipPhaseIIWork.Guarded(_storage);
        if(_decryption is not IZipGuardedEnvelopeEncryptionService)throw new NotSupportedException("Guarded scanner decryption required.");
    }
    internal Task<ZipRetentionRecord?> ReadForScanAsync(ZipParentSnapshot parent,int fileOrdinal,CancellationToken ct)
        => ReadRetentionAsync(parent,fileOrdinal,true,ct,true);
    internal Task<IArtifactContentReadLease> OpenForScanAsync(ZipParentSnapshot parent,int fileOrdinal,IZipEffectBoundaryGuard guard,CancellationToken ct=default)
    {ArgumentNullException.ThrowIfNull(guard);return OpenCoreAsync(parent,fileOrdinal,guard,ct);}
    public Task<IArtifactContentReadLease> OpenAsync(ZipParentSnapshot parent,int fileOrdinal,CancellationToken ct=default)
        => OpenCoreAsync(parent,fileOrdinal,null,ct);
    private async Task<IArtifactContentReadLease> OpenCoreAsync(ZipParentSnapshot parent,int fileOrdinal,IZipEffectBoundaryGuard? guard,CancellationToken ct)
    {
        await ZipPhaseIIWork.CheckAsync(guard,ct);
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(ZipNumericLimits.AttemptTimeout);ct=budget.Token;
        var current=await _parents.ReadAsync(parent.Binding.OperationId,ct);if(current?.Fence!=parent.Fence)throw new ZipFenceException();
        var r=(await ReadRetentionAsync(parent,fileOrdinal,true,ct,guard is not null))!;
        byte[]? plain=null;EncryptedEnvelope? envelope=null;long storedLength=0;
        try
        {
            Namespace(r);
            if(r.State!=ZipRetentionState.Created||r.CandidateHash is null)throw new InvalidDataException("Retention is not readable.");
            var receipt=await _storage.GetReceiptAsync(new(r.Identity.CreateOperationId),ct);
            if(receipt is null||receipt!=r.CreateReceipt)throw new InvalidDataException("Retained receipt changed.");
            await using(var raw=await ZipPhaseIIWork.Read(_storage,Id(r),r.CreateReceipt!.CurrentRevision!.Value,guard,ct)??throw new InvalidDataException("Missing exact retained revision."))
            {
                storedLength=raw.StoredLength;
                if(ZipPhaseIIWork.Hash(raw.Content.Span,guard,ct)!=r.CandidateHash)throw new InvalidDataException("Retained candidate changed.");
                envelope=ZipPhaseIIWork.Decode(raw.Content.Span,_limits,guard,ct);
                plain=await ZipPhaseIIWork.Decrypt(_decryption,envelope,ArtifactEnvelopeContext.Create(Id(r)),_limits,guard,ct);
                if(plain.LongLength!=r.Identity.Length||ZipPhaseIIWork.Hash(plain,guard,ct)!=r.Identity.PlaintextSha256)throw new InvalidDataException("Retained plaintext changed.");
            }
            current=await _parents.ReadAsync(parent.Binding.OperationId,ct);if(current?.Fence!=parent.Fence)throw new ZipFenceException();
            await ZipPhaseIIWork.CheckAsync(guard,ct);
            IArtifactContentReadLease lease;
            using(var acceptance=ZipPhaseIIWork.Accept(guard,ct))
                lease=new ArtifactContentReadLease(Id(r),r.CreateReceipt!.CurrentRevision!.Value,storedLength,plain);
            ct.ThrowIfCancellationRequested();plain=null;return lease;
        }
        catch(Exception error) when((error is InvalidDataException or IOException or CryptographicException)&&guard is null)
        {
            using var review=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);
            await _journal.ReviewRetentionAsync(parent.Fence,r,"RetentionReadEvidenceFailure",review.Token);throw;
        }
        finally{if(plain is not null)CryptographicOperations.ZeroMemory(plain);if(envelope is not null)BoundedEncryptedEnvelopeCodec.Clear(envelope);}
    }
    public async Task<ZipRetentionUpdate> ReleaseAsync(ZipParentSnapshot parent,int fileOrdinal,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(ZipNumericLimits.AttemptTimeout);ct=budget.Token;
        var r=(await ReadRetentionAsync(parent,fileOrdinal,true,ct))!;
        try
        {
        Namespace(r);
        if(r.State==ZipRetentionState.Released)
        { var current=await _parents.ReadAsync(parent.Binding.OperationId,ct);if(current?.Fence!=parent.Fence)throw new ZipFenceException();return new(parent,r); }
        if(r.State==ZipRetentionState.Created)
        { var claimed=await _journal.ClaimRetentionReleaseAsync(parent.Fence,r,ct);parent=claimed.Parent;r=claimed.Retention;await At("ReleasePending"); }
        if(r.State!=ZipRetentionState.ReleasePending)throw new InvalidOperationException("Retention release not authorized.");
        if(r.ReleaseReceipt is null)
        {
            var known=await _storage.GetReceiptAsync(new(r.Identity.ReleaseOperationId),ct);
            await using var physical=known is null?await _storage.PrepareDeleteAsync(Id(r),r.CreateReceipt!.CurrentRevision!.Value,Context(r,true),ct):null;
            await using var prepared=physical is null?null:new CheckpointMutation(physical,()=>At("PhysicalReleased"));
            var released=await _journal.PromoteRetentionReleaseAsync(parent.Fence,r,prepared,known,ct);parent=released.Parent;r=released.Retention;
        }
        await using var cleanup=await _storage.PrepareCandidateCleanupAsync(new(r.Identity.CreateOperationId),r.CandidateHash!,ct);
        await using var checkpointCleanup=new CheckpointCleanup(cleanup,()=>At("CandidateDeleted"));
        var finished=await _journal.FinishRetentionReleaseAsync(parent.Fence,r,checkpointCleanup,ct);await At("Released");return finished;
        }
        catch(Exception error) when(error is InvalidDataException or IOException or ArtifactContentIdempotencyException)
        {
            using var review=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);
            await _journal.ReviewRetentionAsync(parent.Fence,r,"RetentionReleaseEvidenceFailure",review.Token);throw;
        }
    }

    private sealed class CheckpointCleanup(IZipPreparedCandidateCleanup inner,Func<Task> checkpoint) : IZipPreparedCandidateCleanup
    {
        public async Task<ZipCandidateCleanupReceipt> ExecuteAsync(CancellationToken ct=default)
        { var result=await inner.ExecuteAsync(ct);await checkpoint();return result; }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class GuardedCheckpointMutation(IPreparedArtifactContentMutation inner,IZipEffectBoundaryGuard guard,Func<Task> checkpoint) : IZipGuardedPreparedContentMutation
    {
        public IZipEffectBoundaryGuard BoundaryGuard => guard;
        public async Task<ArtifactContentMutationResult> ExecuteAsync(CancellationToken ct=default)
        {var result=await inner.ExecuteAsync(ct);await checkpoint();return result;}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class CheckpointMutation(IPreparedArtifactContentMutation inner,Func<Task> checkpoint) : IPreparedArtifactContentMutation
    {
        public async Task<ArtifactContentMutationResult> ExecuteAsync(CancellationToken ct=default)
        { var result=await inner.ExecuteAsync(ct);await checkpoint();return result; }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask; // Outer prepared handle retains sole disposal ownership.
    }
}
