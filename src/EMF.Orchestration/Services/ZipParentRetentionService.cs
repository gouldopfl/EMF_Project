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
    private async Task ValidateCandidateAsync(ZipParentRetentionRecord r,ReadOnlyMemory<byte> candidate,CancellationToken ct)
    {
        EncryptedEnvelope? envelope=null;byte[]? plain=null;
        try{if(candidate.Length>_storage.MaximumProtectedBytes||(r.CandidateHash is not null&&Hash(candidate.Span)!=r.CandidateHash))throw new InvalidDataException("Frozen parent ciphertext changed.");envelope=BoundedEncryptedEnvelopeCodec.Read(candidate.Span,_limits,ct);plain=await _decryption.DecryptWithContextBoundedAsync(envelope,ArtifactEnvelopeContext.Create(Id(r)),_limits,ct);if(plain.LongLength!=r.Identity.Source.Length||Hash(plain)!=r.Identity.Source.Sha256)throw new InvalidDataException("Protected parent plaintext changed.");}
        finally{if(plain is not null)CryptographicOperations.ZeroMemory(plain);if(envelope is not null)BoundedEncryptedEnvelopeCodec.Clear(envelope);}
    }
    public async Task<ZipParentBinding> RetainAsync(string operation,string parentArtifact,string profile,IArtifactContentReadLease source,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(ZipNumericLimits.AttemptTimeout);ct=timeout.Token;
        if(source.Content.Length!=source.ReturnedLength||source.ReturnedLength>ZipNumericLimits.Parent)throw new InvalidDataException("Parent source exceeds certified profile.");
        var binding=new ZipRetainedBinding(source.ArtifactId.Value,source.Revision.Value,Hash(source.Content.Span),source.ReturnedLength);
        var r=await _journal.ReserveParentRetentionAsync(operation,parentArtifact,binding,profile,_storage.NamespaceId,ct);await At("Reserved");
        byte[]? candidate=null;EncryptedEnvelope? envelope=null;
        try
        {
            Namespace(r);if(r.State==ZipRetentionState.Created)
            {
                if(await _storage.GetReceiptAsync(new(r.Identity.CreateOperationId),ct)!=r.CreateReceipt)throw new InvalidDataException("Parent create receipt changed.");
                await using(var raw=await _storage.ReadProtectedAsync(Id(r),r.CreateReceipt!.CurrentRevision!.Value,ct)??throw new InvalidDataException("Created parent has no exact retained content."))await ValidateCandidateAsync(r,raw.Content,ct);
                await _parents.CreateAsync(Binding(r),ct);return Binding(r);
            }
            if(r.State is ZipRetentionState.RequiresReview or ZipRetentionState.ReleasePending or ZipRetentionState.Released)throw new InvalidDataException("Parent creation is not eligible.");
            var mayEncrypt=false;if(r.State==ZipRetentionState.Reserved){r=await _journal.BeginParentPreparationAsync(r,ct);mayEncrypt=true;await At("PreparationStarted");}
            var known=await _storage.GetReceiptAsync(new(r.Identity.CreateOperationId),ct);candidate=await _storage.ReadCandidateAsync(new(r.Identity.CreateOperationId),ct);
            if(candidate is null&&mayEncrypt&&known is null){envelope=await _encryption.EncryptWithContextAsync(source.Content,ArtifactEnvelopeContext.Create(Id(r)),ct);candidate=BoundedEncryptedEnvelopeCodec.Write(envelope,_limits,ct);await _storage.StageAsync(new(r.Identity.CreateOperationId),candidate,ct);await At("Staged");}
            if(candidate is null)
            {
                if(known is null||r.CandidateHash is null)throw new InvalidDataException("Existing parent preparation has no frozen candidate; regeneration forbidden.");
                await using var raw=await _storage.ReadProtectedAsync(Id(r),known.CurrentRevision??throw new InvalidDataException(),ct)??throw new InvalidDataException("Parent creation receipt has no exact content.");await ValidateCandidateAsync(r,raw.Content,ct);
            }
            else
            {
                await ValidateCandidateAsync(r,candidate,ct);
                if(r.State==ZipRetentionState.Preparing){r=await _journal.BindParentCandidateAsync(r,Hash(candidate),ct);await At("CandidateBound");}
            }
            if(r.State!=ZipRetentionState.CandidateBound)throw new InvalidDataException("Missing frozen parent candidate binding.");
            await using var physical=known is null?await _storage.PrepareCreateAsync(Id(r),candidate!,Context(r,false),ct):null;
            await using var prepared=physical is null?null:new Mutation(physical,()=>At("PhysicalCreated"));
            r=await _journal.PromoteParentRetentionAsync(r,prepared,known,ct);await At("Created");await _parents.CreateAsync(Binding(r),ct);await At("JournalBound");return Binding(r);
        }
        catch(Exception error) when(error is InvalidDataException or IOException or CryptographicException)
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
    public async Task<(ZipParentSnapshot Parent,IArtifactContentReadLease Lease)> OpenAsync(ZipParentSnapshot p,CancellationToken ct=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(ZipNumericLimits.AttemptTimeout);ct=timeout.Token;
        byte[]? plain=null;EncryptedEnvelope? envelope=null;
        try
        {
            p=await _journal.ChargeParentReadAsync(p.Fence,ct);
            var r=await RequiredAsync(p,ct);if(r.State!=ZipRetentionState.Created)throw new InvalidDataException("Parent is not readable.");
            if(await _storage.GetReceiptAsync(new(r.Identity.CreateOperationId),ct)!=r.CreateReceipt)throw new InvalidDataException("Parent create receipt changed.");
            long stored;await using(var raw=await _storage.ReadProtectedAsync(Id(r),r.CreateReceipt!.CurrentRevision!.Value,ct)??throw new InvalidDataException("Missing exact protected parent revision."))
            {stored=raw.StoredLength;if(Hash(raw.Content.Span)!=r.CandidateHash)throw new InvalidDataException("Parent ciphertext changed.");envelope=BoundedEncryptedEnvelopeCodec.Read(raw.Content.Span,_limits,ct);plain=await _decryption.DecryptWithContextBoundedAsync(envelope,ArtifactEnvelopeContext.Create(Id(r)),_limits,ct);if(plain.LongLength!=r.Identity.Source.Length||Hash(plain)!=r.Identity.Source.Sha256)throw new InvalidDataException("Parent plaintext changed.");}
            await RequiredAsync(p,ct);ct.ThrowIfCancellationRequested();await At("Opened");ct.ThrowIfCancellationRequested();var result=new ArtifactContentReadLease(Id(r),r.CreateReceipt!.CurrentRevision!.Value,stored,plain);plain=null;return (p,result);
        }
        catch(Exception error) when(error is InvalidDataException or IOException or CryptographicException)
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
    private sealed class Mutation(IPreparedArtifactContentMutation inner,Func<Task> at):IPreparedArtifactContentMutation
    {public async Task<ArtifactContentMutationResult> ExecuteAsync(CancellationToken ct=default){var result=await inner.ExecuteAsync(ct);await at();return result;}public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
    private sealed class Cleanup(IZipPreparedCandidateCleanup inner,Func<Task> at):IZipPreparedCandidateCleanup
    {public async Task<ZipCandidateCleanupReceipt> ExecuteAsync(CancellationToken ct=default){var result=await inner.ExecuteAsync(ct);await at();return result;}public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
}
