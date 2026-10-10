using EMF.Core.Contracts.Zip;
using Microsoft.Data.Sqlite;
namespace EMF.Persistence.Repositories;
public sealed partial class SqliteZipExtractionJournal
{
    // Test-only synchronous fault boundaries. Production executes SQL only here.
    internal Action<string>? AcknowledgementCheckpoint { get; set; }
    private static ZipRelationshipOccurrence Occurrence(ZipParentSnapshot p,ZipEntryProgress e)=>new(p.Binding.OperationId,p.Binding.ParentArtifactId,p.Fence.PlanHash!,
        e.Plan.CentralOrdinal,e.Plan.FileOrdinal!.Value,e.Plan.ChildOperationId!,e.Plan.ProvisionalArtifactId!,e.Ingestion!.CanonicalArtifactId,e.Ingestion.Sha256);
    private static async Task<List<(long Id,string Type,string Source,string Target,string Properties,string Created)>> OwnedRelationshipsAsync(SqliteConnection c,SqliteTransaction? tx,long contains,long derived,CancellationToken ct)
    {
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT Id,RelationshipType,SourceArtifactId,TargetArtifactId,PropertiesJson,CreatedUtc FROM Relationships WHERE Id IN ($contains,$derived) ORDER BY Id";
        q.Parameters.AddWithValue("$contains",contains);q.Parameters.AddWithValue("$derived",derived);
        var list=new List<(long,string,string,string,string,string)>();using var rows=await q.ExecuteReaderAsync(ct);
        while(await rows.ReadAsync(ct))list.Add((rows.GetInt64(0),rows.GetString(1),rows.GetString(2),rows.GetString(3),rows.GetString(4),rows.GetString(5)));
        return list;
    }
    private static void Relationship(ZipRelationshipOccurrence occurrence,DateTimeOffset created,(long Id,string Type,string Source,string Target,string Properties,string Created) row)
    {
        var contains=row.Type=="Contains";if(row.Id<=0||row.Type is not ("Contains" or "DerivedFrom")||row.Properties!=Json(occurrence)||row.Created!=created.ToString("O")||
            row.Source!=(contains?occurrence.ParentArtifactId:occurrence.CanonicalArtifactId)||row.Target!=(contains?occurrence.CanonicalArtifactId:occurrence.ParentArtifactId))
            throw new InvalidDataException("ZIP relationship occurrence binding changed.");
    }
    private static async Task ValidateAcknowledgementsAsync(SqliteConnection c,SqliteTransaction? tx,ZipParentSnapshot p,IReadOnlyList<ZipEntryProgress> entries,CancellationToken ct)
    {
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT FileOrdinal,ReceiptJson,ContainsId,DerivedFromId FROM ZipExtractionAcknowledgements WHERE ParentOperationId=$op ORDER BY FileOrdinal";q.Parameters.AddWithValue("$op",p.Binding.OperationId);
        var acknowledgements=new List<(int Ordinal,ZipAcknowledgementBinding Ack,long Contains,long Derived)>();
        using(var rows=await q.ExecuteReaderAsync(ct))while(await rows.ReadAsync(ct))
        {if(acknowledgements.Count>=ZipNumericLimits.Entries)throw new InvalidDataException("ZIP acknowledgement count exceeds profile.");acknowledgements.Add((rows.GetInt32(0),Decode<ZipAcknowledgementBinding>(rows.GetString(1)),rows.GetInt64(2),rows.GetInt64(3)));}
        if(acknowledgements.Count!=p.Fence.ConfirmedOrdinal+1)throw new InvalidDataException("ZIP frontier has no exact immutable acknowledgements.");
        foreach(var (ordinal,ack,contains,derived) in acknowledgements)
        {
            var e=entries.SingleOrDefault(e=>e.Plan.FileOrdinal==ordinal);var d=ack.Details;
            if(e?.Ingestion is null||e.Retained is null||e.State is not (ZipEntryState.Acknowledged or ZipEntryState.Released)||ordinal<0||ordinal>p.Fence.ConfirmedOrdinal||
                ack.ParentOperationId!=p.Binding.OperationId||ack.FileOrdinal!=ordinal||ack.PlanHash!=p.Fence.PlanHash||ack.ChildOperationId!=e.Plan.ChildOperationId||ack.CanonicalArtifactId!=e.Ingestion.CanonicalArtifactId||ack.ChildSha256!=e.Retained.Sha256||
                d is null||d.CentralOrdinal!=e.Plan.CentralOrdinal||d.ProvisionalArtifactId!=e.Plan.ProvisionalArtifactId||d.ParentInput!=p.Binding.Input||d.ChildInput!=e.Retained||d.Ingestion!=e.Ingestion||
                d.AdmittedFence.OperationId!=p.Binding.OperationId||d.AdmittedFence.PlanHash!=p.Fence.PlanHash||d.AdmittedFence.ConfirmedOrdinal!=ordinal-1||d.AdmittedFence.Revision<1||d.AdmittedFence.Epoch<1||string.IsNullOrWhiteSpace(d.AdmittedFence.Owner)||
                d.CommittedFence!=d.AdmittedFence with{Revision=checked(d.AdmittedFence.Revision+1),ConfirmedOrdinal=ordinal}||d.CommittedFence.Revision>p.Fence.Revision||
                d.CommittedFence.Epoch>p.Fence.Epoch||contains<=0||derived<=0||contains==derived)throw new InvalidDataException("ZIP acknowledgement binding contradicts durable occurrence.");
            var occurrence=Occurrence(p,e);var relationships=await OwnedRelationshipsAsync(c,tx,contains,derived,ct);
            if(relationships.Count!=2||relationships.Count(r=>r.Id==contains&&r.Type=="Contains")!=1||relationships.Count(r=>r.Id==derived&&r.Type=="DerivedFrom")!=1)
                throw new InvalidDataException("ZIP acknowledgement relationships are missing or contradictory.");
            foreach(var relationship in relationships)Relationship(occurrence,d.CreatedUtc,relationship);
        }
    }
    public async Task<ZipAcknowledgementUpdate> AcknowledgeEntryAsync(ZipFence f,int ordinal,ZipChildIngestionProof expected,CancellationToken ct=default)
    {
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,f,ct);
        if(p.State is not (ZipParentState.Planned or ZipParentState.Processing or ZipParentState.Completed))throw new ZipFenceException();
        var e=p.Entries.SingleOrDefault(e=>e.Plan.FileOrdinal==ordinal)??throw new InvalidDataException("Missing ZIP occurrence.");
        if(e.Ingestion is null||e.Ingestion!=expected)throw new InvalidDataException("ZIP acknowledgement requires exact durable ingestion proof.");
        await ValidateIngestionProofAsync(c,tx,p,e,expected,ct);AcknowledgementCheckpoint?.Invoke("Validated");
        using var q=c.CreateCommand();q.Transaction=tx;q.Parameters.AddWithValue("$op",f.OperationId);q.Parameters.AddWithValue("$file",ordinal);
        if(ordinal<=f.ConfirmedOrdinal)
        {
            q.CommandText="SELECT ReceiptJson,ContainsId,DerivedFromId FROM ZipExtractionAcknowledgements WHERE ParentOperationId=$op AND FileOrdinal=$file";using var row=await q.ExecuteReaderAsync(ct);
            if(!await row.ReadAsync(ct))throw new InvalidDataException("Missing replay acknowledgement.");
            ct.ThrowIfCancellationRequested();if(p.OwnerUntil<=_time.GetUtcNow())throw new ZipFenceException();
            return new(p,Decode<ZipAcknowledgementBinding>(row.GetString(0)),row.GetInt64(1),row.GetInt64(2),true);
        }
        if(ordinal!=checked(f.ConfirmedOrdinal+1)||e.State!=ZipEntryState.Ingested||p.State==ZipParentState.Completed)throw new ZipFenceException();
        var occurrence=Occurrence(p,e);var created=_time.GetUtcNow();
        // ACK relationship IDs are the sole owned reconciliation authority. Without
        // an ACK, foreign/legacy graph rows are never adopted by endpoint or metadata.
        async Task<long> Insert(string type,string source,string target)
        {
            q.CommandText="INSERT INTO Relationships(SourceArtifactId,TargetArtifactId,RelationshipType,CreatedUtc,PropertiesJson) VALUES($source,$target,$type,$time,$properties); SELECT last_insert_rowid();";
            q.Parameters.AddWithValue("$source",source);q.Parameters.AddWithValue("$target",target);q.Parameters.AddWithValue("$type",type);q.Parameters.AddWithValue("$time",created.ToString("O"));q.Parameters.AddWithValue("$properties",Json(occurrence));
            var id=Convert.ToInt64(await q.ExecuteScalarAsync(ct));q.Parameters.RemoveAt("$source");q.Parameters.RemoveAt("$target");q.Parameters.RemoveAt("$type");q.Parameters.RemoveAt("$time");q.Parameters.RemoveAt("$properties");return id;
        }
        var contains=await Insert("Contains",p.Binding.ParentArtifactId,expected.CanonicalArtifactId);AcknowledgementCheckpoint?.Invoke("Contains");
        var derived=await Insert("DerivedFrom",expected.CanonicalArtifactId,p.Binding.ParentArtifactId);AcknowledgementCheckpoint?.Invoke("DerivedFrom");
        var committed=f with{Revision=checked(f.Revision+1),ConfirmedOrdinal=ordinal};
        var ack=new ZipAcknowledgementBinding(f.OperationId,ordinal,f.PlanHash!,e.Plan.ChildOperationId!,expected.CanonicalArtifactId,expected.Sha256,
            new(e.Plan.CentralOrdinal,e.Plan.ProvisionalArtifactId!,p.Binding.Input,e.Retained!,expected,f,committed,created));
        q.CommandText="INSERT INTO ZipExtractionAcknowledgements VALUES($op,$file,$receipt,$contains,$derived)";q.Parameters.AddWithValue("$receipt",Json(ack));q.Parameters.AddWithValue("$contains",contains);q.Parameters.AddWithValue("$derived",derived);await q.ExecuteNonQueryAsync(ct);AcknowledgementCheckpoint?.Invoke("Acknowledgement");
        q.CommandText="UPDATE ZipExtractionEntries SET ProgressJson=$progress WHERE ParentOperationId=$op AND FileOrdinal=$file";q.Parameters.AddWithValue("$progress",Json(e with{State=ZipEntryState.Acknowledged}));await q.ExecuteNonQueryAsync(ct);AcknowledgementCheckpoint?.Invoke("Progress");
        p=p with{State=ZipParentState.Processing,Fence=committed};await SaveAsync(c,tx,p,ct);AcknowledgementCheckpoint?.Invoke("Frontier");ct.ThrowIfCancellationRequested();if(p.OwnerUntil<=_time.GetUtcNow())throw new ZipFenceException();tx.Commit();AcknowledgementCheckpoint?.Invoke("Committed");
        return new((await ReadAsync(f.OperationId,ct))!,ack,contains,derived,false);
    }
}
