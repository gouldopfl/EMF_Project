using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Storage;
namespace EMF.Orchestration.Services;
public sealed class ZipParentRetentionService
{
    private readonly IZipExtractionJournal _parents;private readonly IZipParentRetentionJournal _journal;private readonly IZipPrivateContentStorage _storage;
    private readonly IEnvelopeEncryptionService _encryption;private readonly IBoundedEnvelopeDecryptionService _decryption;
    private readonly EnvelopeDecryptionLimits _limits=new(ZipNumericLimits.Parent);
    internal Func<string,Task>? Checkpoint{get;set;}
    public ZipParentRetentionService(IZipExtractionJournal parents,IZipParentRetentionJournal journal,IZipPrivateContentStorage storage,IEnvelopeEncryptionService encryption)
    {(_parents,_journal,_storage,_encryption)=(parents,journal,storage,encryption);_decryption=encryption as IBoundedEnvelopeDecryptionService??throw new NotSupportedException("Bounded parent decryption required.");if(storage.MaximumProtectedBytes!=BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Parent))throw new InvalidOperationException("Private parent store must enforce the certified 32 MiB profile.");}
    private Task At(string name)=>Checkpoint?.Invoke(name)??Task.CompletedTask;
    private static string Hash(ReadOnlySpan<byte> bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static ArtifactId Id(ZipParentRetentionRecord r)=>new(r.Identity.ObjectId);
    private static ArtifactContentMutationContext Context(ZipParentRetentionRecord r,bool release)=>new(new(release?r.Identity.ReleaseOperationId:r.Identity.CreateOperationId),new ArtifactContentOwnershipToken(r.Identity.OwnerToken));
    private void Namespace(ZipParentRetentionRecord r){if(r.Identity.NamespaceId!=_storage.NamespaceId)throw new InvalidDataException("Protected parent namespace changed.");}
    private ZipParentBinding Binding(ZipParentRetentionRecord r)=>new(r.Identity.ParentOperationId,r.Identity.ParentArtifactId,new(r.Identity.ObjectId,r.CreateReceipt!.CurrentRevision!.Value.Value,r.Identity.Source.Sha256,r.Identity.Source.Length),r.Identity.ProfileHash);
    private async Task ValidateCandidateAsync(ZipParentRetentionRecord r,ReadOnlyMemory<byte> candidate,CancellationToken ct,IZipEffectBoundaryGuard? guard=null)
    {
        EncryptedEnvelope? envelope=null;byte[]? plain=null;
        try{await ZipPhaseIIWork.CheckAsync(guard,ct);if(candidate.Length>_storage.MaximumProtectedBytes||(r.CandidateHash is not null&&ZipPhaseIIWork.Hash(candidate.Span,guard,ct)!=r.CandidateHash))throw new InvalidDataException("Frozen parent ciphertext changed.");envelope=ZipPhaseIIWork.Decode(candidate.Span,_limits,guard,ct);plain=await ZipPhaseIIWork.Decrypt(_decryption,envelope,ArtifactEnvelopeContext.Create(Id(r)),_limits,guard,ct);if(plain.LongLength!=r.Identity.Source.Length||ZipPhaseIIWork.Hash(plain,guard,ct)!=r.Identity.Source.Sha256)throw new InvalidDataException("Protected parent plaintext changed.");}
        finally{if(plain is not null)CryptographicOperations.ZeroMemory(plain);if(envelope is not null)BoundedEncryptedEnvelopeCodec.Clear(envelope);}
    }
    internal Task<ZipParentBinding> RetainAsync(string operation,string parentArtifact,string profile,IArtifactContentReadLease source,CancellationToken ct=default)
        => RetainCoreAsync(operation,parentArtifact,profile,source,null,ct);
    internal Task<ZipParentBinding> RetainAsync(string operation,string parentArtifact,string profile,IArtifactContentReadLease source,ZipParentRetentionAttempt attempt,CancellationToken ct)
        => RetainCoreAsync(operation,parentArtifact,profile,source,attempt ?? throw new ArgumentNullException(nameof(attempt)),ct);
    private async Task<ZipParentBinding> RetainCoreAsync(string operation,string parentArtifact,string profile,IArtifactContentReadLease source,ZipParentRetentionAttempt? attempt,CancellationToken ct)
    {
        await using var guard=attempt?.Boundary();
        using var linked=guard is null ? CancellationTokenSource.CreateLinkedTokenSource(ct) : CancellationTokenSource.CreateLinkedTokenSource(ct,guard.Invalidated);
        ct=linked.Token;
        await ZipPhaseIIWork.CheckAsync(guard,ct);
        ct.ThrowIfCancellationRequested();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(ZipNumericLimits.AttemptTimeout);ct=timeout.Token;
        if(attempt is not null&&_storage.NamespaceId!=attempt.Current.Binding.ParentNamespaceId)throw new UnauthorizedAccessException("Parent namespace differs from admission.");
        ZipRetainedBinding binding;
        ReadOnlyMemory<byte> sourceContent;
        using(var sourceAccess=ZipPhaseIIWork.Accept(guard,ct))
        {
        if(source.Content.Length!=source.ReturnedLength||source.ReturnedLength>ZipNumericLimits.Parent)throw new InvalidDataException("Parent source exceeds certified profile.");
        sourceContent=source.Content;
        binding=new ZipRetainedBinding(source.ArtifactId.Value,source.Revision.Value,Hash(sourceContent.Span),source.ReturnedLength);
        }
        if(attempt is not null)attempt.Source=binding;
        var r=guard is null ? await _journal.ReserveParentRetentionAsync(operation,parentArtifact,binding,profile,_storage.NamespaceId,ct)
            : await _journal.ReserveParentRetentionAsync(operation,parentArtifact,binding,profile,_storage.NamespaceId,guard,ct);
        if(attempt is not null)attempt.Retention=r;
        await At("Reserved");
        var result=await FinishCreationAsync(r,sourceContent,ct,guard,attempt);
        if(guard is not null)await guard.FinishAsync(ct);
        return result;
    }
    // The caller supplies its original admitted identity. Recovery never reserves
    // an identity, rereads canonical source, or re-encrypts missing candidates.
    // Authentication remains the responsibility of the explicit host boundary.
    internal Task<ZipParentBinding> RecoverCreationAsync(ZipParentRetentionIdentity expected,CancellationToken ct=default)
        => RecoverCoreAsync(expected,null,ct);
    internal Task<ZipParentBinding> RecoverCreationAsync(ZipParentRetentionIdentity expected,ZipParentRetentionAttempt attempt,CancellationToken ct)
        => RecoverCoreAsync(expected,attempt ?? throw new ArgumentNullException(nameof(attempt)),ct);
    private async Task<ZipParentBinding> RecoverCoreAsync(ZipParentRetentionIdentity expected,ZipParentRetentionAttempt? attempt,CancellationToken ct)
    {
        await using var guard=attempt?.Boundary();
        using var linked=guard is null ? CancellationTokenSource.CreateLinkedTokenSource(ct) : CancellationTokenSource.CreateLinkedTokenSource(ct,guard.Invalidated);
        ct=linked.Token;
        await ZipPhaseIIWork.CheckAsync(guard,ct);
        ArgumentNullException.ThrowIfNull(expected);
        if(attempt is not null&&(_storage.NamespaceId!=attempt.Current.Binding.ParentNamespaceId||expected.NamespaceId!=_storage.NamespaceId))throw new UnauthorizedAccessException("Parent recovery namespace changed.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(ZipNumericLimits.AttemptTimeout);ct=timeout.Token;
        using var permit=await ZipParentReadAdmission.ProcessWide.AcquireAsync(ct);
        var retained=await _journal.ReadParentRetentionAsync(expected.ParentOperationId,ct)??throw new InvalidDataException("Missing original parent retention.");
        if(retained.Identity!=expected)throw new InvalidDataException("Original parent recovery identity changed.");
        if(retained.State==ZipRetentionState.Reserved)throw new InvalidDataException("Unprepared parent still requires an authorized exact source read.");
        if(attempt is not null){attempt.Source=retained.Identity.Source;attempt.Retention=retained;}
        var result=await FinishCreationAsync(retained,null,ct,guard,attempt);
        if(guard is not null)await guard.FinishAsync(ct);
        return result;
    }
    private async Task<ZipParentBinding> FinishCreationAsync(ZipParentRetentionRecord r,ReadOnlyMemory<byte>? source,CancellationToken ct,IZipEffectBoundaryGuard? guard=null,ZipParentRetentionAttempt? attempt=null)
    {
        var operation=r.Identity.ParentOperationId;
        byte[]? candidate=null;EncryptedEnvelope? envelope=null;
        try
        {
            await ZipPhaseIIWork.CheckAsync(guard,ct);
            Namespace(r);if(r.State==ZipRetentionState.Created)
            {
                if(await _storage.GetReceiptAsync(new(r.Identity.CreateOperationId),ct)!=r.CreateReceipt)throw new InvalidDataException("Parent create receipt changed.");
                await using(var raw=await ZipPhaseIIWork.Read(_storage,Id(r),r.CreateReceipt!.CurrentRevision!.Value,guard,ct)??throw new InvalidDataException("Created parent has no exact retained content."))await ValidateCandidateAsync(r,raw.Content,ct,guard);
                if(guard is null)await _parents.CreateAsync(Binding(r),ct);else await _parents.CreateAsync(Binding(r),guard,ct);return Binding(r);
            }
            if(r.State is ZipRetentionState.RequiresReview or ZipRetentionState.ReleasePending or ZipRetentionState.Released)throw new InvalidDataException("Parent creation is not eligible.");
            var mayEncrypt=false;if(r.State==ZipRetentionState.Reserved){r=guard is null ? await _journal.BeginParentPreparationAsync(r,ct) : await _journal.BeginParentPreparationAsync(r,guard,ct);if(attempt is not null)attempt.Retention=r;mayEncrypt=true;await At("PreparationStarted");}
            var known=await _storage.GetReceiptAsync(new(r.Identity.CreateOperationId),ct);candidate=await ZipPhaseIIWork.Candidate(_storage,new(r.Identity.CreateOperationId),guard,ct);
            if(candidate is null&&mayEncrypt&&known is null){envelope=await ZipPhaseIIWork.Encrypt(_encryption,source??throw new InvalidDataException("Recovery cannot regenerate parent ciphertext."),ArtifactEnvelopeContext.Create(Id(r)),guard,ct);candidate=ZipPhaseIIWork.Encode(envelope,_limits,guard,ct);await ZipPhaseIIWork.Stage(_storage,new(r.Identity.CreateOperationId),candidate,guard,ct);await At("Staged");}
            if(candidate is null)
            {
                if(known is null||r.CandidateHash is null)throw new InvalidDataException("Existing parent preparation has no frozen candidate; regeneration forbidden.");
                await using var raw=await ZipPhaseIIWork.Read(_storage,Id(r),known.CurrentRevision??throw new InvalidDataException(),guard,ct)??throw new InvalidDataException("Parent creation receipt has no exact content.");await ValidateCandidateAsync(r,raw.Content,ct,guard);
            }
            else
            {
                await ValidateCandidateAsync(r,candidate,ct,guard);
                if(r.State==ZipRetentionState.Preparing){r=guard is null ? await _journal.BindParentCandidateAsync(r,ZipPhaseIIWork.Hash(candidate,guard,ct),ct) : await _journal.BindParentCandidateAsync(r,ZipPhaseIIWork.Hash(candidate,guard,ct),guard,ct);if(attempt is not null)attempt.Retention=r;await At("CandidateBound");}
            }
            if(r.State!=ZipRetentionState.CandidateBound)throw new InvalidDataException("Missing frozen parent candidate binding.");
            await using var physical=known is null?await ZipPhaseIIWork.Prepare(_storage,Id(r),candidate!,Context(r,false),guard,ct):null;
            await using IPreparedArtifactContentMutation? prepared=physical is null?null:guard is null ? new Mutation(physical,()=>At("PhysicalCreated")) : new GuardedMutation(physical,guard,()=>At("PhysicalCreated"));
            r=guard is null ? await _journal.PromoteParentRetentionAsync(r,prepared,known,ct) : await _journal.PromoteParentRetentionAsync(r,prepared,known,guard,ct);if(attempt is not null)attempt.Retention=r;await At("Created");if(guard is null)await _parents.CreateAsync(Binding(r),ct);else await _parents.CreateAsync(Binding(r),guard,ct);await At("JournalBound");return Binding(r);
        }
        catch(Exception error) when((error is InvalidDataException or IOException or CryptographicException)&&guard is null)
        {using var cleanup=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);var current=await _journal.ReadParentRetentionAsync(operation,cleanup.Token);if(current is not null&&(await _parents.ReadAsync(operation,cleanup.Token)) is null)await _journal.ReviewParentCreationAsync(current,"ParentCreationEvidenceFailure",cleanup.Token);throw;}
        finally{if(candidate is not null)CryptographicOperations.ZeroMemory(candidate);if(envelope is not null)BoundedEncryptedEnvelopeCodec.Clear(envelope);}
    }
    private async Task<ZipParentRetentionRecord> RequiredAsync(ZipParentSnapshot p,CancellationToken ct)
    {
        var current=await _parents.ReadAsync(p.Binding.OperationId,ct);if(current?.Fence!=p.Fence)throw new ZipFenceException();
        var r=await _journal.ReadParentRetentionAsync(p.Binding.OperationId,ct)??throw new InvalidDataException("Missing protected parent ownership evidence.");Namespace(r);
        if(r.CreateReceipt is null||Binding(r)!=p.Binding)throw new InvalidDataException("Protected parent differs from admitted input.");
        if(await _storage.GetReceiptAsync(new(r.Identity.CreateOperationId),ct)!=r.CreateReceipt||r.ReleaseReceipt is not null&&await _storage.GetReceiptAsync(new(r.Identity.ReleaseOperationId),ct)!=r.ReleaseReceipt)throw new InvalidDataException("Protected parent durable receipt changed.");return r;
    }
    internal Task<(ZipParentSnapshot Parent,IArtifactContentReadLease Lease)> OpenAsync(ZipParentSnapshot p,CancellationToken ct=default)
        => OpenCoreAsync(p,null,ct);
    public Task<(ZipParentSnapshot Parent,IArtifactContentReadLease Lease)> OpenAsync(ZipParentSnapshot p,ZipExecutionAttempt attempt,CancellationToken ct=default)
        => OpenCoreAsync(p,attempt ?? throw new ArgumentNullException(nameof(attempt)),ct);
    private async Task<(ZipParentSnapshot Parent,IArtifactContentReadLease Lease)> OpenCoreAsync(ZipParentSnapshot p,ZipExecutionAttempt? attempt,CancellationToken ct)
    {
        await using var guard=attempt?.ParentReadBoundary(() => p);
        using var linked=guard is null ? CancellationTokenSource.CreateLinkedTokenSource(ct) : CancellationTokenSource.CreateLinkedTokenSource(ct,guard.Invalidated);
        ct=linked.Token;
        await ZipPhaseIIWork.CheckAsync(guard,ct);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(ZipNumericLimits.AttemptTimeout);ct=timeout.Token;
        byte[]? plain=null;EncryptedEnvelope? envelope=null;
        try
        {
            p=guard is null ? await _journal.ChargeParentReadAsync(p.Fence,ct) : await _journal.ChargeParentReadAsync(p.Fence,guard,ct);
            var r=await RequiredAsync(p,ct);if(r.State!=ZipRetentionState.Created)throw new InvalidDataException("Parent is not readable.");
            if(await _storage.GetReceiptAsync(new(r.Identity.CreateOperationId),ct)!=r.CreateReceipt)throw new InvalidDataException("Parent create receipt changed.");
            long stored;await using(var raw=await ZipPhaseIIWork.Read(_storage,Id(r),r.CreateReceipt!.CurrentRevision!.Value,guard,ct)??throw new InvalidDataException("Missing exact protected parent revision."))
            {stored=raw.StoredLength;if(ZipPhaseIIWork.Hash(raw.Content.Span,guard,ct)!=r.CandidateHash)throw new InvalidDataException("Parent ciphertext changed.");envelope=ZipPhaseIIWork.Decode(raw.Content.Span,_limits,guard,ct);plain=await ZipPhaseIIWork.Decrypt(_decryption,envelope,ArtifactEnvelopeContext.Create(Id(r)),_limits,guard,ct);if(plain.LongLength!=r.Identity.Source.Length||ZipPhaseIIWork.Hash(plain,guard,ct)!=r.Identity.Source.Sha256)throw new InvalidDataException("Parent plaintext changed.");}
            await RequiredAsync(p,ct);ct.ThrowIfCancellationRequested();await At("Opened");if(guard is not null)await guard.FinishAsync(ct);ct.ThrowIfCancellationRequested();var result=new ArtifactContentReadLease(Id(r),r.CreateReceipt!.CurrentRevision!.Value,stored,plain);plain=null;return (p,result);
        }
        catch(Exception error) when((error is InvalidDataException or IOException or CryptographicException)&&attempt is null)
        {using var cleanup=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);await _journal.ReviewParentEvidenceAsync(p.Fence,"ParentReadEvidenceFailure",cleanup.Token);throw;}
        finally{if(plain is not null)CryptographicOperations.ZeroMemory(plain);if(envelope is not null)BoundedEncryptedEnvelopeCodec.Clear(envelope);}
    }
    public async Task<ZipParentReleaseUpdate> ReleaseAsync(ZipParentSnapshot p,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(ZipNumericLimits.AttemptTimeout);ct=timeout.Token;
        ZipParentRetentionRecord? r=null;
        try
        {
            r=await RequiredAsync(p,ct);if(r.State==ZipRetentionState.Released){if(p.State!=ZipParentState.Released)throw new InvalidDataException("Parent release journal contradicts receipt.");return new(p,r);}
            if(r.State==ZipRetentionState.Created){var claimed=await _journal.ClaimParentReleaseAsync(p.Fence,r,ct);p=claimed.Parent;r=claimed.Retention;await At("ReleasePending");}
            if(r.State!=ZipRetentionState.ReleasePending)throw new InvalidOperationException("Parent release is not authorized.");
            if(r.ReleaseReceipt is null)
            {
                var known=await _storage.GetReceiptAsync(new(r.Identity.ReleaseOperationId),ct);
                await using var physical=known is null?await _storage.PrepareDeleteAsync(Id(r),r.CreateReceipt!.CurrentRevision!.Value,Context(r,true),ct):null;
                await using var prepared=physical is null?null:new Mutation(physical,()=>At("PhysicalReleased"));
                var deleted=await _journal.PromoteParentReleaseAsync(p.Fence,r,prepared,known,ct);p=deleted.Parent;r=deleted.Retention;
            }
            await using var cleanup=await _storage.PrepareCandidateCleanupAsync(new(r.Identity.CreateOperationId),r.CandidateHash!,ct);
            await using var hook=new Cleanup(cleanup,()=>At("CandidateDeleted"));var result=await _journal.FinishParentReleaseAsync(p.Fence,r,hook,ct);await At("Released");return result;
        }
        catch(Exception error) when(error is InvalidDataException or IOException or ArtifactContentIdempotencyException)
        {using var cleanup=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);await _journal.ReviewParentEvidenceAsync(p.Fence,"ParentReleaseEvidenceFailure",cleanup.Token);throw;}
    }
    private sealed class GuardedMutation(IPreparedArtifactContentMutation inner,IZipEffectBoundaryGuard guard,Func<Task> at):IZipGuardedPreparedContentMutation
    {
        public IZipEffectBoundaryGuard BoundaryGuard=>guard;
        public async Task<ArtifactContentMutationResult> ExecuteAsync(CancellationToken ct=default)
        {var result=await inner.ExecuteAsync(ct);await at();return result;}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class Mutation(IPreparedArtifactContentMutation inner,Func<Task> at):IPreparedArtifactContentMutation
    {public async Task<ArtifactContentMutationResult> ExecuteAsync(CancellationToken ct=default){var result=await inner.ExecuteAsync(ct);await at();return result;}public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
    private sealed class Cleanup(IZipPreparedCandidateCleanup inner,Func<Task> at):IZipPreparedCandidateCleanup
    {public async Task<ZipCandidateCleanupReceipt> ExecuteAsync(CancellationToken ct=default){var result=await inner.ExecuteAsync(ct);await at();return result;}public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
}
