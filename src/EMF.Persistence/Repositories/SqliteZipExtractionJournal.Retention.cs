using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Repositories;

public sealed partial class SqliteZipExtractionJournal
{
    private static async Task<ZipRetentionRecord?> RetentionAsync(SqliteConnection c,SqliteTransaction? tx,string parent,int ordinal,CancellationToken ct)
    {
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT BindingJson,StateJson FROM ZipExtractionRetentions WHERE ParentOperationId=$op AND FileOrdinal=$file";
        q.Parameters.AddWithValue("$op",parent);q.Parameters.AddWithValue("$file",ordinal);
        using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;
        var record=Decode<ZipRetentionRecord>(r.GetString(1));
        if(Json(record.Identity)!=r.GetString(0)||record.Identity.ParentOperationId!=parent||record.Identity.FileOrdinal!=ordinal||
            !Enum.IsDefined(record.State)||record.Revision<1||record.Identity.Length<0||record.Identity.Length>ZipNumericLimits.Child)
            throw new InvalidDataException("Invalid ZIP retention binding.");
        if(record.State is ZipRetentionState.CandidateBound or ZipRetentionState.Created or ZipRetentionState.ReleasePending or ZipRetentionState.Released && record.CandidateHash is null)
            throw new InvalidDataException("Missing frozen ZIP candidate.");
        if(record.State is ZipRetentionState.Created or ZipRetentionState.ReleasePending or ZipRetentionState.Released && record.CreateReceipt?.CurrentRevision is null)
            throw new InvalidDataException("Missing retained create receipt.");
        if(record.State==ZipRetentionState.Released && (record.ReleaseReceipt is null||record.CandidateCleanup is null))
            throw new InvalidDataException("Missing retention cleanup evidence.");
        if(record.CreateReceipt is not null)Receipt(record,record.CreateReceipt,false);
        if(record.ReleaseReceipt is not null)Receipt(record,record.ReleaseReceipt,true);
        if(record.CandidateCleanup is not null&&(record.CandidateCleanup.CreateOperationId!=record.Identity.CreateOperationId||record.CandidateCleanup.CandidateHash!=record.CandidateHash))
            throw new InvalidDataException("Invalid ZIP candidate cleanup evidence.");
        return record;
    }
    public async Task<ZipRetentionRecord?> ReadRetentionAsync(string parentOperation,int fileOrdinal,CancellationToken ct=default)
    { await using var c=await OpenAsync(ct);return await RetentionAsync(c,null,parentOperation,fileOrdinal,ct); }
    public async Task<ZipRetentionUpdate> ReserveRetentionAsync(ZipFence fence,int ordinal,string namespaceId,string hash,CancellationToken ct=default)
    {
        if(hash.Length!=64||namespaceId.Length!=64)throw new InvalidDataException("Invalid ZIP retained fingerprint/namespace.");
        _=Convert.FromHexString(hash);_=Convert.FromHexString(namespaceId);
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,fence,ct);
        if(p.State is not (ZipParentState.Planned or ZipParentState.Processing)||ordinal!=fence.ConfirmedOrdinal+1)throw new ZipFenceException();
        var entry=p.Entries.Single(e=>e.Plan.FileOrdinal==ordinal).Plan;
        var old=await RetentionAsync(c,tx,fence.OperationId,ordinal,ct);
        if(old is not null)
        {
            if(old.Identity.PlaintextSha256!=hash||old.Identity.NamespaceId!=namespaceId)throw new InvalidDataException("ZIP retention request changed.");
            return new(p,old);
        }
        var id=new ZipRetentionIdentity(fence.OperationId,ordinal,"zip-quarantine-"+Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),namespaceId,hash,entry.ExpandedLength);
        var retention=new ZipRetentionRecord(id);using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText="INSERT INTO ZipExtractionRetentions VALUES($op,$file,$binding,$state)";
        q.Parameters.AddWithValue("$op",fence.OperationId);q.Parameters.AddWithValue("$file",ordinal);q.Parameters.AddWithValue("$binding",Json(id));q.Parameters.AddWithValue("$state",Json(retention));
        await q.ExecuteNonQueryAsync(ct);p=p with{Fence=p.Fence with{Revision=checked(p.Fence.Revision+1)}};
        await SaveAsync(c,tx,p,ct);tx.Commit();return new(p,retention);
    }
    private async Task<ZipRetentionUpdate> ChangeRetentionAsync(ZipFence fence,ZipRetentionRecord expected,
        Func<SqliteConnection,SqliteTransaction,ZipParentSnapshot,Task<ZipRetentionRecord>> change,CancellationToken ct)
    {
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,fence,ct);
        var old=await RetentionAsync(c,tx,fence.OperationId,expected.Identity.FileOrdinal,ct);
        if(old is null||Json(old)!=Json(expected)||old.Identity.ParentOperationId!=fence.OperationId)throw new ZipFenceException();
        var next=await change(c,tx,p);
        if(next.Identity!=old.Identity)throw new InvalidDataException("ZIP retention identity changed.");
        next=next with{Revision=checked(old.Revision+1)};
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="UPDATE ZipExtractionRetentions SET StateJson=$json WHERE ParentOperationId=$op AND FileOrdinal=$file";
        q.Parameters.AddWithValue("$json",Json(next));q.Parameters.AddWithValue("$op",fence.OperationId);q.Parameters.AddWithValue("$file",expected.Identity.FileOrdinal);await q.ExecuteNonQueryAsync(ct);
        p=p with{State=next.State==ZipRetentionState.RequiresReview?ZipParentState.RequiresReview:p.State,Fence=p.Fence with{Revision=checked(p.Fence.Revision+1)}};
        await SaveAsync(c,tx,p,ct);tx.Commit();return new(p,next);
    }
    public Task<ZipRetentionUpdate> BeginRetentionPreparationAsync(ZipFence f,ZipRetentionRecord e,CancellationToken ct=default)=>
        ChangeRetentionAsync(f,e,(_,_,p)=>Task.FromResult(p.State is (ZipParentState.Planned or ZipParentState.Processing)&&e.State==ZipRetentionState.Reserved?e with{State=ZipRetentionState.Preparing}:throw new InvalidOperationException("Encryption preparation already started.")),ct);
    public Task<ZipRetentionUpdate> BindRetentionCandidateAsync(ZipFence f,ZipRetentionRecord e,string hash,CancellationToken ct=default)
    {
        if(hash.Length!=64)throw new InvalidDataException("Invalid candidate hash.");_=Convert.FromHexString(hash);
        return ChangeRetentionAsync(f,e,(_,_,p)=>Task.FromResult(p.State is (ZipParentState.Planned or ZipParentState.Processing)&&e.State==ZipRetentionState.Preparing?e with{State=ZipRetentionState.CandidateBound,CandidateHash=hash}:throw new InvalidOperationException("Candidate cannot be rebound.")),ct);
    }
    private static void Receipt(ZipRetentionRecord e,ArtifactContentMutationReceipt r,bool release)
    {
        if(r.ArtifactId.Value!=e.Identity.ObjectId||r.OperationId.Value!=(release?e.Identity.ReleaseOperationId:e.Identity.CreateOperationId)||
            r.OwnershipToken?.Value!=e.Identity.OwnerToken||r.Kind!=(release?ArtifactContentMutationKind.Delete:ArtifactContentMutationKind.Create)||
            r.Outcome!=(release?ArtifactContentMutationOutcome.Deleted:ArtifactContentMutationOutcome.Created)||
            r.AuditObligationVersion!=1||r.AuditEventId is not null||
            (release?(r.PriorRevision!=e.CreateReceipt?.CurrentRevision||r.CurrentRevision is null||r.CurrentRevision==r.PriorRevision):(r.PriorRevision is not null||r.CurrentRevision is null)))
            throw new InvalidDataException("ZIP physical receipt contradicts retained binding.");
    }
    public Task<ZipRetentionUpdate> PromoteRetentionAsync(ZipFence f,ZipRetentionRecord e,IPreparedArtifactContentMutation? prepared,
        ArtifactContentMutationReceipt? known,CancellationToken ct=default)=>ChangeRetentionAsync(f,e,async(_,_,p)=>
        {
            if(e.State!=ZipRetentionState.CandidateBound||p.State is not (ZipParentState.Planned or ZipParentState.Processing))throw new InvalidOperationException("Retention promotion is not admitted.");
            var receipt=known??(await (prepared??throw new InvalidDataException("Missing prepared mutation.")).ExecuteAsync(ct)).Receipt;
            Receipt(e,receipt,false);return e with{State=ZipRetentionState.Created,CreateReceipt=receipt};
        },ct);
    private static async Task ReleaseEligibleAsync(SqliteConnection c,SqliteTransaction tx,ZipParentSnapshot p,ZipRetentionRecord e,CancellationToken ct)
    {
        if(p.State is not (ZipParentState.Planned or ZipParentState.Processing or ZipParentState.Completed) || p.Fence.ConfirmedOrdinal<e.Identity.FileOrdinal)
            throw new InvalidOperationException("ZIP retained child remains required; release denied.");
        using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText="SELECT ReceiptJson,ContainsId,DerivedFromId FROM ZipExtractionAcknowledgements WHERE ParentOperationId=$op AND FileOrdinal=$file";
        q.Parameters.AddWithValue("$op",p.Binding.OperationId);q.Parameters.AddWithValue("$file",e.Identity.FileOrdinal);
        ZipAcknowledgementBinding ack;long contains,derived;
        using(var r=await q.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct))throw new InvalidDataException("Missing durable ZIP acknowledgement.");
            ack=Decode<ZipAcknowledgementBinding>(r.GetString(0));contains=r.GetInt64(1);derived=r.GetInt64(2);
        }
        var entry=p.Entries.Single(x=>x.Plan.FileOrdinal==e.Identity.FileOrdinal).Plan;
        if(ack.ParentOperationId!=p.Binding.OperationId||ack.FileOrdinal!=e.Identity.FileOrdinal||ack.PlanHash!=p.Fence.PlanHash||
            ack.ChildOperationId!=entry.ChildOperationId||ack.ChildSha256!=e.Identity.PlaintextSha256||string.IsNullOrWhiteSpace(ack.CanonicalArtifactId))
            throw new InvalidDataException("ZIP acknowledgement contradicts retained child.");
        q.Parameters.AddWithValue("$canonical",ack.CanonicalArtifactId);q.Parameters.AddWithValue("$parent",p.Binding.ParentArtifactId);
        q.Parameters.AddWithValue("$hash",ack.ChildSha256);q.Parameters.AddWithValue("$contains",contains);q.Parameters.AddWithValue("$derived",derived);
        q.CommandText="""
            SELECT (SELECT COUNT(*) FROM Relationships WHERE Id=$contains AND SourceArtifactId=$parent AND TargetArtifactId=$canonical AND RelationshipType='Contains')
                 + (SELECT COUNT(*) FROM Relationships WHERE Id=$derived AND SourceArtifactId=$canonical AND TargetArtifactId=$parent AND RelationshipType='DerivedFrom')
                 + (SELECT COUNT(*) FROM Artifacts a JOIN ArtifactMutationAuthority m ON m.ArtifactId=a.Id
                    WHERE a.Id=$canonical AND a.FingerprintAlgorithm='SHA-256' AND a.FingerprintValue=$hash AND m.IsAdopted=1)
            """;
        if(Convert.ToInt32(await q.ExecuteScalarAsync(ct))!=3)throw new InvalidDataException("ZIP release adoption/relationship proof is missing or contradictory.");
    }
    public Task<ZipRetentionUpdate> ClaimRetentionReleaseAsync(ZipFence f,ZipRetentionRecord e,CancellationToken ct=default)=>ChangeRetentionAsync(f,e,async(c,tx,p)=>
    {
        await ReleaseEligibleAsync(c,tx,p,e,ct);if(e.State!=ZipRetentionState.Created)throw new InvalidOperationException("Retention release cannot be claimed.");
        return e with{State=ZipRetentionState.ReleasePending};
    },ct);
    public Task<ZipRetentionUpdate> PromoteRetentionReleaseAsync(ZipFence f,ZipRetentionRecord e,IPreparedArtifactContentMutation? prepared,
        ArtifactContentMutationReceipt? known,CancellationToken ct=default)=>ChangeRetentionAsync(f,e,async(c,tx,p)=>
    {
        await ReleaseEligibleAsync(c,tx,p,e,ct);if(e.State!=ZipRetentionState.ReleasePending||e.ReleaseReceipt is not null)throw new InvalidOperationException("Release not pending.");
        var receipt=known??(await (prepared??throw new InvalidDataException("Missing prepared deletion.")).ExecuteAsync(ct)).Receipt;
        Receipt(e,receipt,true);return e with{ReleaseReceipt=receipt};
    },ct);
    public Task<ZipRetentionUpdate> FinishRetentionReleaseAsync(ZipFence f,ZipRetentionRecord e,IZipPreparedCandidateCleanup cleanup,CancellationToken ct=default)=>ChangeRetentionAsync(f,e,async(c,tx,p)=>
    {
        await ReleaseEligibleAsync(c,tx,p,e,ct);
        if(e.State!=ZipRetentionState.ReleasePending||e.ReleaseReceipt is null)throw new InvalidOperationException("Physical release not proven.");
        var receipt=await cleanup.ExecuteAsync(ct);
        if(receipt.CreateOperationId!=e.Identity.CreateOperationId||receipt.CandidateHash!=e.CandidateHash)throw new InvalidDataException("Candidate cleanup binding changed.");
        return e with{State=ZipRetentionState.Released,CandidateCleanup=receipt};
    },ct);
    public Task<ZipRetentionUpdate> ReviewRetentionAsync(ZipFence f,ZipRetentionRecord e,string reason,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>128||reason.Any(char.IsControl))throw new ArgumentException("Safe reason required.");
        return ChangeRetentionAsync(f,e,(_,_,_)=>Task.FromResult(e with{State=ZipRetentionState.RequiresReview,SafeReason=reason}),ct);
    }
}
