using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Repositories;

public sealed partial class SqliteZipExtractionJournal
{
    // Test-only synchronous checkpoint at the two guarded ZIP journal commit windows.
    internal Action<string>? ProtectedReleaseCheckpoint { get; set; }

    private async Task<DateTimeOffset> ValidateProtectedReleaseAdmissionAsync(SqliteConnection c,
        SqliteTransaction tx,ZipParentSnapshot parent,ZipProtectedReleaseAuthorityRequest request,CancellationToken ct)
    {
        var current=await ReadAdmissionCoreAsync(c,tx,parent.Binding.OperationId,ct)
            ??throw new UnauthorizedAccessException("Missing current ZIP protected-release admission.");
        var capability=request.Purpose==ZipOwnershipPurpose.InitialExecution
            ?ZipAuthorityCapabilities.Admit:ZipAuthorityCapabilities.Recover;
        if(current!=request.Admission||current.State!=ZipAdmissionState.ParentBound||
            current.Parent!=parent.Binding||current.Approval is null||current.LatestAuthorityEvidence is null||
            (current.Approval.Capabilities&capability)==0||
            (current.LatestAuthorityEvidence.Capabilities&capability)==0)
            throw new UnauthorizedAccessException("ZIP protected-release admission or purpose changed.");
        await ValidateExecutionAdmissionAsync(c,tx,parent,ct);
        return CurrentEvidenceExpiry(current);
    }

    private async Task ValidateProtectedReleaseAsync(SqliteConnection c,SqliteTransaction tx,
        ZipParentSnapshot parent,ZipRetentionRecord retained,ZipProtectedReleaseAuthorityRequest request,CancellationToken ct)
    {
        var ordinal=retained.Identity.FileOrdinal;
        if(request.Parent!=parent.Binding||request.Fence!=parent.Fence||request.Parent!=request.Admission.Parent||
            request.GrantValidUntilUtc<=_time.GetUtcNow()||string.IsNullOrWhiteSpace(request.ExecutingActorId)||
            request.Purpose is not (ZipOwnershipPurpose.InitialExecution or ZipOwnershipPurpose.Recovery)||
            parent.State is not (ZipParentState.Planned or ZipParentState.Processing)||
            parent.Fence.ConfirmedOrdinal!=ordinal||request.Retention!=retained||
            retained.State is not (ZipRetentionState.Created or ZipRetentionState.ReleasePending)||
            retained.CandidateCleanup is not null||retained.State==ZipRetentionState.Created&&retained.ReleaseReceipt is not null)
            throw new UnauthorizedAccessException("ZIP protected release is outside its exact live boundary.");
        var entry=parent.Entries.SingleOrDefault(e=>e.Plan.FileOrdinal==ordinal)
            ??throw new InvalidDataException("Missing ZIP release occurrence.");
        var (ack,contains,derived)=await ReleaseAcknowledgementAsync(c,tx,parent.Binding.OperationId,ordinal,ct);
        if(entry.State!=ZipEntryState.Acknowledged||entry.Plan!=request.Occurrence||
            entry.Retained!=request.ChildInput||entry.Ingestion!=request.Ingestion||
            ack!=request.Acknowledgement||contains!=request.ContainsId||derived!=request.DerivedFromId||
            ack.CanonicalArtifactId!=request.Ingestion.CanonicalArtifactId||
            ack.ChildSha256!=request.Ingestion.Sha256||
            entry.Retained?.ContentId!=retained.Identity.ObjectId||
            entry.Retained?.Revision!=retained.CreateReceipt?.CurrentRevision?.Value||
            entry.Retained?.Sha256!=retained.Identity.PlaintextSha256||
            entry.Retained?.Length!=retained.Identity.Length||
            retained.Identity.ParentOperationId!=parent.Binding.OperationId||
            string.IsNullOrWhiteSpace(retained.Identity.NamespaceId))
            throw new InvalidDataException("ZIP protected-release evidence changed.");
        await ReleaseEligibleAsync(c,tx,parent,retained,ct);
        await ValidateProtectedReleaseAdmissionAsync(c,tx,parent,request,ct);
    }

    public Task<ZipRetentionUpdate> ClaimRetentionReleaseAsync(ZipProtectedReleaseAuthorityRequest request,
        IZipEffectBoundaryGuard guard,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(request);ArgumentNullException.ThrowIfNull(guard);
        return ChangeRetentionAsync(request.Fence,request.Retention,async(c,tx,p)=>
        {
            await ValidateProtectedReleaseAsync(c,tx,p,request.Retention,request,ct);
            if(request.Retention.State!=ZipRetentionState.Created)throw new InvalidOperationException("ZIP release already claimed.");
            return request.Retention with{State=ZipRetentionState.ReleasePending};
        },ct,guard,request,"ClaimCommitWindow");
    }

    public Task<ZipRetentionUpdate> PromoteRetentionReleaseAsync(ZipProtectedReleaseAuthorityRequest request,
        IPreparedArtifactContentMutation? prepared,ArtifactContentMutationReceipt? known,
        IZipPrivateContentStorage storage,IZipEffectBoundaryGuard guard,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(request);ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(guard);
        return ChangeRetentionAsync(request.Fence,request.Retention,async(c,tx,p)=>
        {
            await ValidateProtectedReleaseAsync(c,tx,p,request.Retention,request,ct);
            GuardedPrepared(prepared,known,guard);
            var retained=request.Retention;
            if(retained.State!=ZipRetentionState.ReleasePending||retained.ReleaseReceipt is not null||
                storage.NamespaceId!=retained.Identity.NamespaceId)
                throw new InvalidOperationException("ZIP protected release receipt is not eligible.");
            var receipt=known??(await (prepared??throw new InvalidDataException("Missing guarded ZIP deletion.")).ExecuteAsync(ct)).Receipt;
            Receipt(retained,receipt,true);
            var actual=await storage.GetReceiptGuardedAsync(new(retained.Identity.ReleaseOperationId),guard,ct);
            if(actual!=receipt)throw new InvalidDataException("ZIP protected deletion has no exact durable receipt.");
            var tombstone=await storage.ReadTombstoneRevisionGuardedAsync(new ArtifactId(retained.Identity.ObjectId),guard,ct);
            if(tombstone!=receipt.CurrentRevision)
                throw new InvalidDataException("ZIP protected tombstone does not match its delete receipt.");
            return retained with{ReleaseReceipt=receipt};
        },ct,guard,request,"PromoteCommitWindow");
    }

    public async Task<ZipRetentionUpdate> ObserveProtectedReleaseReceiptAsync(
        ZipProtectedReleaseAuthorityRequest request,IZipPrivateContentStorage storage,
        IZipEffectBoundaryGuard guard,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(request);ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(guard);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,guard.Invalidated);ct=linked.Token;
        await ValidateEffectAsync(guard,ct);
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        var parent=await FencedAsync(c,tx,request.Fence,ct);
        var retained=await RetentionAsync(c,tx,request.Parent.OperationId,request.Retention.Identity.FileOrdinal,ct)
            ??throw new InvalidDataException("Missing ZIP retained child.");
        await ValidateProtectedReleaseAsync(c,tx,parent,retained,request,ct);
        if(request.Purpose!=ZipOwnershipPurpose.Recovery||retained.State!=ZipRetentionState.ReleasePending||
            retained.ReleaseReceipt is null||retained.CandidateCleanup is not null||
            storage.NamespaceId!=retained.Identity.NamespaceId)
            throw new UnauthorizedAccessException("ZIP receipt observation requires exact Recovery authority.");
        var actual=await storage.GetReceiptGuardedAsync(new(retained.Identity.ReleaseOperationId),guard,ct);
        if(actual!=retained.ReleaseReceipt)throw new InvalidDataException("ZIP release receipt differs from protected catalog.");
        var tombstone=await storage.ReadTombstoneRevisionGuardedAsync(new ArtifactId(retained.Identity.ObjectId),guard,ct);
        if(tombstone!=retained.ReleaseReceipt.CurrentRevision)
            throw new InvalidDataException("ZIP released object's tombstone differs from its receipt.");
        await guard.RevalidateAsync(ct);
        ct.ThrowIfCancellationRequested();
        return new(parent,retained);
    }
}
