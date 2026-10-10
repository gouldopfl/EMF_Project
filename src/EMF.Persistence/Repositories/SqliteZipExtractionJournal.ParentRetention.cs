using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using Microsoft.Data.Sqlite;
namespace EMF.Persistence.Repositories;
public sealed partial class SqliteZipExtractionJournal
{
    private static void ParentReceipt(ZipParentRetentionRecord r,ArtifactContentMutationReceipt receipt,bool release)
    {
        var i=r.Identity;
        if(receipt.ArtifactId.Value!=i.ObjectId||receipt.OperationId.Value!=(release?i.ReleaseOperationId:i.CreateOperationId)||receipt.OwnershipToken?.Value!=i.OwnerToken||
            receipt.Kind!=(release?ArtifactContentMutationKind.Delete:ArtifactContentMutationKind.Create)||receipt.Outcome!=(release?ArtifactContentMutationOutcome.Deleted:ArtifactContentMutationOutcome.Created)||
            receipt.AuditObligationVersion!=1||receipt.AuditEventId is not null||(release?(receipt.PriorRevision!=r.CreateReceipt?.CurrentRevision||receipt.CurrentRevision is null||receipt.CurrentRevision==receipt.PriorRevision):(receipt.PriorRevision is not null||receipt.CurrentRevision is null)))
            throw new InvalidDataException("ZIP protected parent receipt changed.");
    }
    private static async Task<ZipParentRetentionRecord?> ParentRetentionAsync(SqliteConnection c,SqliteTransaction? tx,string operation,CancellationToken ct)
    {
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT BindingJson,StateJson FROM ZipExtractionRetentions WHERE ParentOperationId=$op AND FileOrdinal=-1";q.Parameters.AddWithValue("$op",operation);
        using var rows=await q.ExecuteReaderAsync(ct);if(!await rows.ReadAsync(ct))return null;var r=Decode<ZipParentRetentionRecord>(rows.GetString(1));
        if(r.Identity is null||r.Identity.Source is null||r.Identity.Source.Sha256 is null||r.Identity.ProfileHash is null||r.Identity.NamespaceId is null||r.Identity.ObjectId is null)throw new InvalidDataException("Missing ZIP protected parent identity fields.");
        if(Json(r.Identity)!=rows.GetString(0)||r.Identity.ParentOperationId!=operation||r.Identity.Source.Length<0||r.Identity.Source.Length>ZipNumericLimits.Parent||
            r.Revision<1||!Enum.IsDefined(r.State)||r.Identity.Source.Sha256.Length!=64||r.Identity.ProfileHash.Length!=64||r.Identity.NamespaceId.Length!=64||!r.Identity.ObjectId.StartsWith("zip-quarantine-",StringComparison.Ordinal))throw new InvalidDataException("ZIP protected parent binding changed.");
        foreach(var hash in new[]{r.Identity.Source.Sha256,r.Identity.ProfileHash,r.Identity.NamespaceId})_=Convert.FromHexString(hash);
        foreach(var id in new[]{r.Identity.ParentOperationId,r.Identity.ParentArtifactId,r.Identity.Source.ContentId,r.Identity.Source.Revision,r.Identity.ObjectId,r.Identity.CreateOperationId,r.Identity.ReleaseOperationId,r.Identity.OwnerToken})ArtifactContentIdentity.Validate(id);
        if(r.CandidateHash is not null){if(r.CandidateHash.Length!=64)throw new InvalidDataException("Invalid parent candidate hash.");_=Convert.FromHexString(r.CandidateHash);}
        if(r.State is ZipRetentionState.CandidateBound or ZipRetentionState.Created or ZipRetentionState.ReleasePending or ZipRetentionState.Released && r.CandidateHash is null)throw new InvalidDataException("Missing frozen parent ciphertext binding.");
        if(r.State is ZipRetentionState.Created or ZipRetentionState.ReleasePending or ZipRetentionState.Released && r.CreateReceipt?.CurrentRevision is null)throw new InvalidDataException("Missing parent creation receipt.");
        if(r.CreateReceipt is not null)ParentReceipt(r,r.CreateReceipt,false);if(r.ReleaseReceipt is not null)ParentReceipt(r,r.ReleaseReceipt,true);
        if(r.State==ZipRetentionState.Released&&(r.ReleaseReceipt is null||r.CandidateCleanup is null))throw new InvalidDataException("Missing parent cleanup evidence.");
        if(r.CandidateCleanup is not null&&(r.CandidateCleanup.CreateOperationId!=r.Identity.CreateOperationId||r.CandidateCleanup.CandidateHash!=r.CandidateHash))throw new InvalidDataException("Parent candidate cleanup changed.");
        return r;
    }
    public async Task<ZipParentRetentionRecord?> ReadParentRetentionAsync(string operation,CancellationToken ct=default)
    {await using var c=await OpenAsync(ct);return await ParentRetentionAsync(c,null,operation,ct);}
    public async Task<ZipParentRetentionRecord> ReserveParentRetentionAsync(string operation,string parentArtifact,ZipRetainedBinding source,string profile,string namespaceId,CancellationToken ct=default)
    {
        Validate(new(operation,parentArtifact,source,profile));foreach(var id in new[]{operation,parentArtifact,source.ContentId,source.Revision})ArtifactContentIdentity.Validate(id);
        if(namespaceId.Length!=64)throw new InvalidDataException("Invalid parent private namespace.");_=Convert.FromHexString(namespaceId);
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var old=await ParentRetentionAsync(c,tx,operation,ct);
        if(old is not null){if(old.Identity.ParentArtifactId!=parentArtifact||old.Identity.Source!=source||old.Identity.ProfileHash!=profile||old.Identity.NamespaceId!=namespaceId)throw new InvalidDataException("Parent retention request changed.");return old;}
        using var q=c.CreateCommand();q.Transaction=tx;q.Parameters.AddWithValue("$op",operation);q.CommandText="SELECT COUNT(*) FROM ZipExtractionParents WHERE OperationId=$op";
        if(Convert.ToInt32(await q.ExecuteScalarAsync(ct))!=0)throw new InvalidDataException("Existing parent has no owned protected copy; replacement forbidden.");
        var r=new ZipParentRetentionRecord(new(operation,parentArtifact,source,profile,"zip-quarantine-"+Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),namespaceId));
        q.CommandText="INSERT INTO ZipExtractionRetentions VALUES($op,-1,$binding,$state)";q.Parameters.AddWithValue("$binding",Json(r.Identity));q.Parameters.AddWithValue("$state",Json(r));await q.ExecuteNonQueryAsync(ct);tx.Commit();return r;
    }
    private static async Task SaveParentRetentionAsync(SqliteConnection c,SqliteTransaction tx,ZipParentRetentionRecord r,CancellationToken ct)
    {using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="UPDATE ZipExtractionRetentions SET StateJson=$state WHERE ParentOperationId=$op AND FileOrdinal=-1";q.Parameters.AddWithValue("$op",r.Identity.ParentOperationId);q.Parameters.AddWithValue("$state",Json(r));await q.ExecuteNonQueryAsync(ct);}
    private async Task<ZipParentRetentionRecord> ChangeParentCreationAsync(ZipParentRetentionRecord expected,Func<Task<ZipParentRetentionRecord>> change,CancellationToken ct)
    {
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var old=await ParentRetentionAsync(c,tx,expected.Identity.ParentOperationId,ct);
        if(old!=expected)throw new ZipFenceException();using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT COUNT(*) FROM ZipExtractionParents WHERE OperationId=$op";q.Parameters.AddWithValue("$op",expected.Identity.ParentOperationId);
        if(Convert.ToInt32(await q.ExecuteScalarAsync(ct))!=0)throw new ZipFenceException();
        var next=(await change()) with{Revision=checked(expected.Revision+1)};if(next.Identity!=expected.Identity)throw new InvalidDataException();await SaveParentRetentionAsync(c,tx,next,ct);ct.ThrowIfCancellationRequested();tx.Commit();return next;
    }
    public Task<ZipParentRetentionRecord> BeginParentPreparationAsync(ZipParentRetentionRecord e,CancellationToken ct=default)=>ChangeParentCreationAsync(e,()=>Task.FromResult(e.State==ZipRetentionState.Reserved?e with{State=ZipRetentionState.Preparing}:throw new InvalidOperationException("Parent preparation already started.")),ct);
    public Task<ZipParentRetentionRecord> BindParentCandidateAsync(ZipParentRetentionRecord e,string hash,CancellationToken ct=default)
    {if(hash.Length!=64)throw new InvalidDataException();_=Convert.FromHexString(hash);return ChangeParentCreationAsync(e,()=>Task.FromResult(e.State==ZipRetentionState.Preparing?e with{State=ZipRetentionState.CandidateBound,CandidateHash=hash}:throw new InvalidOperationException("Parent candidate cannot be rebound.")),ct);}
    public Task<ZipParentRetentionRecord> PromoteParentRetentionAsync(ZipParentRetentionRecord e,IPreparedArtifactContentMutation? prepared,ArtifactContentMutationReceipt? known,CancellationToken ct=default)=>ChangeParentCreationAsync(e,async()=>
    {if(e.State!=ZipRetentionState.CandidateBound)throw new InvalidOperationException();var receipt=known??(await (prepared??throw new InvalidDataException()).ExecuteAsync(ct)).Receipt;ParentReceipt(e,receipt,false);return e with{State=ZipRetentionState.Created,CreateReceipt=receipt};},ct);
    public Task<ZipParentRetentionRecord> ReviewParentCreationAsync(ZipParentRetentionRecord e,string reason,CancellationToken ct=default)=>ChangeParentCreationAsync(e,()=>Task.FromResult(e with{State=ZipRetentionState.RequiresReview,SafeReason=reason}),ct);
    private static void ParentBinding(ZipParentSnapshot p,ZipParentRetentionRecord r)
    {if(r.Identity.ParentOperationId!=p.Binding.OperationId||r.Identity.ParentArtifactId!=p.Binding.ParentArtifactId||r.Identity.ProfileHash!=p.Binding.ProfileHash||r.CreateReceipt?.CurrentRevision is null||p.Binding.Input!=new ZipRetainedBinding(r.Identity.ObjectId,r.CreateReceipt.CurrentRevision.Value.Value,r.Identity.Source.Sha256,r.Identity.Source.Length))throw new InvalidDataException("Parent journal contradicts protected copy.");}
    private static async Task ParentCompleteEvidenceAsync(SqliteConnection c,SqliteTransaction? tx,ZipParentSnapshot p,CancellationToken ct)
    {
        if(p.Plan is null||p.Fence.ConfirmedOrdinal!=p.Entries.Count(e=>!e.Plan.IsDirectory)-1||p.Entries.Any(e=>!e.Plan.IsDirectory&&e.State is not (ZipEntryState.Acknowledged or ZipEntryState.Released)))throw new InvalidOperationException("Parent still requires child work.");
        foreach(var e in p.Entries.Where(e=>!e.Plan.IsDirectory))
        {var r=await RetentionAsync(c,tx,p.Binding.OperationId,e.Plan.FileOrdinal!.Value,ct);if(r?.State!=ZipRetentionState.Released||r.Identity.ObjectId!=e.Retained?.ContentId||r.Identity.Length!=e.Retained?.Length||r.Identity.PlaintextSha256!=e.Ingestion?.Sha256||r.CreateReceipt?.CurrentRevision?.Value!=e.Retained?.Revision)throw new InvalidDataException("Parent completion has no exact child cleanup evidence.");}
    }
    private static async Task ValidateParentRetentionAsync(SqliteConnection c,SqliteTransaction? tx,ZipParentSnapshot p,CancellationToken ct)
    {
        if(p.State==ZipParentState.RequiresReview)return; // No execution authority; preserve unreadable parent evidence.
        var r=await ParentRetentionAsync(c,tx,p.Binding.OperationId,ct);
        if(r is null){if(p.State is ZipParentState.Completed or ZipParentState.Released)throw new InvalidDataException("Terminal ZIP state has no owned parent retention.");return;}
        ParentBinding(p,r);
        if(p.State is ZipParentState.AdmissionPending or ZipParentState.Planned or ZipParentState.Processing && r.State!=ZipRetentionState.Created)throw new InvalidDataException("Active parent has no readable owned retention.");
        if(p.State is ZipParentState.Completed or ZipParentState.Released)
        {
            await ParentCompleteEvidenceAsync(c,tx,p,ct);
            if(p.State==ZipParentState.Released?r.State!=ZipRetentionState.Released:r.State is not (ZipRetentionState.Created or ZipRetentionState.ReleasePending))throw new InvalidDataException("Terminal parent state contradicts cleanup receipts.");
        }
    }
    // The private reader bypass is exclusively a non-destructive review transition.
    // All plan/frontier/scan/ingestion/ACK/budget proof remains mandatory. It can
    // never provide active ownership, completion, ingestion or release authority.
    public async Task<ZipParentSnapshot> ReviewParentEvidenceAsync(ZipFence f,string reason,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>128||reason.Any(char.IsControl))throw new ArgumentException("Bounded safe reason required.");
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await ReadAsync(c,tx,f.OperationId,ct,parentEvidenceReviewOnly:true)??throw new ZipFenceException();
        if(p.Fence!=f||p.OwnerUntil<=_time.GetUtcNow())throw new ZipFenceException();
        if(p.State==ZipParentState.RequiresReview)return p;
        p=p with{State=ZipParentState.RequiresReview,Fence=f with{Revision=checked(f.Revision+1)}};await SaveAsync(c,tx,p,ct);
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="UPDATE ZipExtractionParents SET SafeReason=$reason WHERE OperationId=$op";q.Parameters.AddWithValue("$reason",reason);q.Parameters.AddWithValue("$op",f.OperationId);await q.ExecuteNonQueryAsync(ct);
        ct.ThrowIfCancellationRequested();if(p.OwnerUntil<=_time.GetUtcNow())throw new ZipFenceException();tx.Commit();return p;
    }
    public async Task<ZipParentSnapshot> ClaimParentEvidenceReviewAsync(string operation,string owner,TimeSpan duration,CancellationToken ct=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);if(duration<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(duration));
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await ReadAsync(c,tx,operation,ct,parentEvidenceReviewOnly:true)??throw new InvalidDataException("Missing ZIP operation.");
        if(p.OwnerUntil>_time.GetUtcNow()&&p.Fence.Owner!=owner)throw new ZipFenceException();
        p=p with{State=ZipParentState.RequiresReview,OwnerUntil=_time.GetUtcNow()+duration,Fence=p.Fence with{Owner=owner,Epoch=checked(p.Fence.Epoch+1),Revision=checked(p.Fence.Revision+1)}};await SaveAsync(c,tx,p,ct);
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="UPDATE ZipExtractionParents SET SafeReason='ParentRecoveryEvidenceFailure' WHERE OperationId=$op";q.Parameters.AddWithValue("$op",operation);await q.ExecuteNonQueryAsync(ct);ct.ThrowIfCancellationRequested();tx.Commit();return p;
    }
    public async Task<ZipParentSnapshot> ChargeParentReadAsync(ZipFence f,CancellationToken ct=default)
    {
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,f,ct);
        if(p.State is not (ZipParentState.AdmissionPending or ZipParentState.Planned or ZipParentState.Processing))throw new InvalidOperationException("Parent payload work is not authorized.");
        var r=await ParentRetentionAsync(c,tx,f.OperationId,ct)??throw new InvalidDataException("Missing parent retention.");ParentBinding(p,r);
        if(r.State!=ZipRetentionState.Created)throw new InvalidDataException("Parent is not readable.");
        var charge=new ZipExtractionCharge(Guid.NewGuid().ToString("N"),p.Binding.Input.Revision,-1,ParentReadBytes:p.Binding.Input.Length);
        var budget=AddCharge(p.Budget,charge);ValidateBudget(budget);
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="INSERT INTO ZipExtractionCharges VALUES($op,$id,$reservation,-1,$json)";
        q.Parameters.AddWithValue("$op",f.OperationId);q.Parameters.AddWithValue("$id",charge.Id);q.Parameters.AddWithValue("$reservation",charge.ReservationId);q.Parameters.AddWithValue("$json",Json(charge));await q.ExecuteNonQueryAsync(ct);
        p=p with{Budget=budget,Fence=f with{Revision=checked(f.Revision+1)}};await SaveAsync(c,tx,p,ct);
        ct.ThrowIfCancellationRequested();if(p.OwnerUntil<=_time.GetUtcNow())throw new ZipFenceException();tx.Commit();return p;
    }
    public async Task<ZipParentSnapshot> CompleteParentAsync(ZipFence f,CancellationToken ct=default)
    {
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,f,ct);var r=await ParentRetentionAsync(c,tx,f.OperationId,ct)??throw new InvalidDataException("Missing protected parent ownership.");ParentBinding(p,r);
        await ParentCompleteEvidenceAsync(c,tx,p,ct);if(p.State is ZipParentState.Completed or ZipParentState.Released)return p;
        if(p.State is not (ZipParentState.Planned or ZipParentState.Processing)||r.State!=ZipRetentionState.Created)throw new InvalidOperationException("Parent completion is not eligible.");
        p=p with{State=ZipParentState.Completed,Fence=f with{Revision=checked(f.Revision+1)}};await SaveAsync(c,tx,p,ct);ct.ThrowIfCancellationRequested();if(p.OwnerUntil<=_time.GetUtcNow())throw new ZipFenceException();tx.Commit();return p;
    }
    private async Task<ZipParentReleaseUpdate> ChangeParentReleaseAsync(ZipFence f,ZipParentRetentionRecord expected,Func<Task<ZipParentRetentionRecord>> change,CancellationToken ct,bool review=false)
    {
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,f,ct);var r=await ParentRetentionAsync(c,tx,f.OperationId,ct);
        if(r!=expected)throw new ZipFenceException();ParentBinding(p,expected);if(!review){if(p.State!=ZipParentState.Completed)throw new InvalidOperationException("Parent release requires durable completion.");await ParentCompleteEvidenceAsync(c,tx,p,ct);}
        if(p.OwnerUntil<=_time.GetUtcNow())throw new ZipFenceException();ct.ThrowIfCancellationRequested();var next=(await change()) with{Revision=checked(expected.Revision+1)};
        await SaveParentRetentionAsync(c,tx,next,ct);p=p with{State=review?ZipParentState.RequiresReview:next.State==ZipRetentionState.Released?ZipParentState.Released:p.State,Fence=f with{Revision=checked(f.Revision+1)}};
        await SaveAsync(c,tx,p,ct);ct.ThrowIfCancellationRequested();if(p.OwnerUntil<=_time.GetUtcNow())throw new ZipFenceException();tx.Commit();return new(p,next);
    }
    public Task<ZipParentReleaseUpdate> ClaimParentReleaseAsync(ZipFence f,ZipParentRetentionRecord e,CancellationToken ct=default)=>ChangeParentReleaseAsync(f,e,()=>Task.FromResult(e.State==ZipRetentionState.Created?e with{State=ZipRetentionState.ReleasePending}:throw new InvalidOperationException()),ct);
    public Task<ZipParentReleaseUpdate> PromoteParentReleaseAsync(ZipFence f,ZipParentRetentionRecord e,IPreparedArtifactContentMutation? prepared,ArtifactContentMutationReceipt? known,CancellationToken ct=default)=>ChangeParentReleaseAsync(f,e,async()=>
    {if(e.State!=ZipRetentionState.ReleasePending||e.ReleaseReceipt is not null)throw new InvalidOperationException();var receipt=known??(await (prepared??throw new InvalidDataException()).ExecuteAsync(ct)).Receipt;ParentReceipt(e,receipt,true);return e with{ReleaseReceipt=receipt};},ct);
    public Task<ZipParentReleaseUpdate> FinishParentReleaseAsync(ZipFence f,ZipParentRetentionRecord e,IZipPreparedCandidateCleanup cleanup,CancellationToken ct=default)=>ChangeParentReleaseAsync(f,e,async()=>
    {if(e.State!=ZipRetentionState.ReleasePending||e.ReleaseReceipt is null)throw new InvalidOperationException();var receipt=await cleanup.ExecuteAsync(ct);if(receipt.CreateOperationId!=e.Identity.CreateOperationId||receipt.CandidateHash!=e.CandidateHash)throw new InvalidDataException();return e with{State=ZipRetentionState.Released,CandidateCleanup=receipt};},ct);
    public Task<ZipParentReleaseUpdate> ReviewParentRetentionAsync(ZipFence f,ZipParentRetentionRecord e,string reason,CancellationToken ct=default)=>ChangeParentReleaseAsync(f,e,()=>Task.FromResult(e with{State=ZipRetentionState.RequiresReview,SafeReason=reason}),ct,true);
}
