using System.Text;
using System.Text.Json;
using EMF.Core.Contracts.Zip;
using EMF.Persistence.Storage;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Repositories;

public sealed partial class SqliteZipExtractionJournal : IZipExtractionJournal, IZipRetentionJournal, IZipExtractionExecutionJournal, IZipScanJournal, IZipChildIngestionJournal, IZipAcknowledgementJournal,IZipParentRetentionJournal
{
    private readonly string _path;
    private readonly IContentStoragePlatform _platform = ContentStoragePlatform.Select();
    private readonly TimeProvider _time;
    public SqliteZipExtractionJournal(string evidenceDatabasePath, TimeProvider? time = null)
    {
        _path = Path.GetFullPath(evidenceDatabasePath);
        _time = time ?? TimeProvider.System;
        _platform.RequirePlatform();
        for (var p = _path; p is not null; p = Path.GetDirectoryName(p))
            if (new FileInfo(p).LinkTarget is not null) throw new IOException("ZIP database contains a symbolic link.");
    }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value) ?? throw new InvalidDataException("Missing ZIP journal value.");
    private async Task<SqliteConnection> OpenAsync(CancellationToken ct, bool verify = true)
    {
        _platform.ValidatePrivatePermissions(_path);
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path,
            Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5 }.ToString());
        try
        {
            await c.OpenAsync(ct);
            using var q = c.CreateCommand();
            q.CommandText = "PRAGMA synchronous=EXTRA; PRAGMA busy_timeout=5000;";
            await q.ExecuteNonQueryAsync(ct);
            q.CommandText = "PRAGMA journal_mode";
            if (!string.Equals(await q.ExecuteScalarAsync(ct) as string, "delete", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ZIP journal requires DELETE journal mode.");
            if (verify) await VerifyAsync(c, null, ct);
            return c;
        }
        catch { await c.DisposeAsync(); throw; }
    }
    private static string SchemaSql
    {
        get
        {
            var sql = new StringBuilder(ZipExtractionSchema.Sql);
            foreach (var table in ZipExtractionSchema.ImmutableTables.Append("ZipExtractionParents"))
            {
                var duplicate = table switch
                {
                    "ZipExtractionParents" => "OperationId=NEW.OperationId",
                    "ZipExtractionEntries" => "(ParentOperationId=NEW.ParentOperationId AND CentralOrdinal=NEW.CentralOrdinal) OR (ParentOperationId=NEW.ParentOperationId AND FileOrdinal=NEW.FileOrdinal) OR ChildOperationId=NEW.ChildOperationId OR ArtifactId=NEW.ArtifactId",
                    "ZipExtractionReservations" => "ParentOperationId=NEW.ParentOperationId AND ReservationId=NEW.ReservationId",
                    "ZipExtractionAcknowledgements" => "ParentOperationId=NEW.ParentOperationId AND FileOrdinal=NEW.FileOrdinal",
                    _ => "ParentOperationId=NEW.ParentOperationId AND ContentId=NEW.ContentId AND Revision=NEW.Revision"
                };
                sql.Append($"\nCREATE TRIGGER {table}NoDelete BEFORE DELETE ON {table} BEGIN SELECT RAISE(ABORT,'Immutable ZIP evidence'); END;");
                sql.Append($"\nCREATE TRIGGER {table}NoReplace BEFORE INSERT ON {table} WHEN EXISTS(SELECT 1 FROM {table} WHERE {duplicate}) BEGIN SELECT RAISE(ABORT,'Immutable ZIP evidence'); END;");
                if (table is not "ZipExtractionEntries" and not "ZipExtractionParents")
                    sql.Append($"\nCREATE TRIGGER {table}NoUpdate BEFORE UPDATE ON {table} BEGIN SELECT RAISE(ABORT,'Immutable ZIP evidence'); END;");
            }
            return sql.ToString();
        }
    }
    private static Dictionary<string,string> BuildExpected(string sql)
    {
        using var c = new SqliteConnection("Data Source=:memory:"); c.Open();
        using var q = c.CreateCommand(); q.CommandText = sql; q.ExecuteNonQuery();
        q.CommandText = "SELECT name,sql FROM sqlite_master WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%'";
        using var r = q.ExecuteReader(); var result = new Dictionary<string, string>();
        while (r.Read()) result.Add(r.GetString(0), r.GetString(1));
        return result;
    }
    private static readonly Lazy<Dictionary<string,string>> ExpectedSchemaV1=new(()=>BuildExpected(SchemaSql));
    private static readonly Lazy<Dictionary<string,string>> ExpectedSchemaV2=new(()=>BuildExpected(SchemaSql+"\n"+ZipRetentionSchema.Sql));
    private static readonly Lazy<Dictionary<string,string>> ExpectedSchemaV3=new(()=>BuildExpected(SchemaSql+"\n"+ZipRetentionSchema.Sql+"\n"+ZipExtractionWorkSchema.Sql));
    private static readonly Lazy<Dictionary<string,string>> ExpectedSchema=new(()=>BuildExpected(SchemaSql+"\n"+ZipRetentionSchema.Sql+"\n"+ZipExtractionWorkSchema.Sql+"\n"+ZipScanSchema.Sql));
    private static async Task VerifyAsync(SqliteConnection c, SqliteTransaction? tx, CancellationToken ct,int version=4)
    {
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT COALESCE(MAX(Version),0) FROM ZipExtractionSchema";
        if (Convert.ToInt32(await q.ExecuteScalarAsync(ct)) != version) throw new InvalidDataException("Unsupported ZIP schema.");
        q.CommandText = "SELECT name,sql FROM sqlite_master WHERE sql IS NOT NULL";
        using var r = await q.ExecuteReaderAsync(ct); var observed = new Dictionary<string, string>();
        while (await r.ReadAsync(ct)) observed[r.GetString(0)] = r.GetString(1);
        foreach (var pair in (version==1?ExpectedSchemaV1.Value:version==2?ExpectedSchemaV2.Value:version==3?ExpectedSchemaV3.Value:ExpectedSchema.Value))
            if (!observed.TryGetValue(pair.Key, out var sql) || sql != pair.Value)
                throw new InvalidDataException("ZIP schema or invariant trigger changed.");
    }
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _platform.FlushDirectory(Path.GetDirectoryName(_path)!, verifyFileSystem: true);
        await using var c = await OpenAsync(ct, false);
        using var tx = c.BeginTransaction(deferred: false); using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Artifacts','Relationships','Provenance')";
        if (Convert.ToInt32(await q.ExecuteScalarAsync(ct)) != 3) throw new InvalidOperationException("Initialized Evidence database required.");
        q.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='ZipExtractionSchema'";
        if (Convert.ToInt32(await q.ExecuteScalarAsync(ct)) == 0)
        { q.CommandText = SchemaSql; await q.ExecuteNonQueryAsync(ct); }
        q.CommandText="SELECT COALESCE(MAX(Version),0) FROM ZipExtractionSchema";
        var version=Convert.ToInt32(await q.ExecuteScalarAsync(ct));
        if(version==1)
        {
            await VerifyAsync(c,tx,ct,1);
            q.CommandText=ZipRetentionSchema.Sql;await q.ExecuteNonQueryAsync(ct);version=2;
        }
        if(version==2)
        { await VerifyAsync(c,tx,ct,2);q.CommandText=ZipExtractionWorkSchema.Sql;await q.ExecuteNonQueryAsync(ct);version=3; }
        if(version==3)
        { await VerifyAsync(c,tx,ct,3);q.CommandText=ZipScanSchema.Sql;await q.ExecuteNonQueryAsync(ct); }
        await VerifyAsync(c, tx, ct); tx.Commit();
    }
    private static async Task<ZipParentSnapshot?> ReadAsync(SqliteConnection c, SqliteTransaction? tx, string op, CancellationToken ct, bool parentEvidenceReviewOnly=false)
    {
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT BindingJson,Owner,Epoch,Revision,OwnerUntil,State,PlanJson,PlanHash,ConfirmedOrdinal,BudgetJson FROM ZipExtractionParents WHERE OperationId=$op";
        q.Parameters.AddWithValue("$op", op);
        ZipParentSnapshot result;
        using (var r = await q.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            result = new(Decode<ZipParentBinding>(r.GetString(0)),
                new(op,r.GetString(1),r.GetInt64(2),r.GetInt64(3),r.IsDBNull(7) ? null : r.GetString(7),r.GetInt32(8)),
                DateTimeOffset.Parse(r.GetString(4)),(ZipParentState)r.GetInt32(5),
                r.IsDBNull(6) ? null : Decode<ZipPlan>(r.GetString(6)),Decode<ZipBudget>(r.GetString(9)),[]);
        }
        Validate(result.Binding);
        if (result.Binding.OperationId != op || !Enum.IsDefined(result.State) || result.Fence.Epoch < 0 || result.Fence.Revision < 1)
            throw new InvalidDataException("Invalid ZIP parent state or binding.");
        ValidateBudget(result.Budget);
        q.CommandText = "SELECT ProgressJson,PlanJson,CentralOrdinal,FileOrdinal,ChildOperationId,ArtifactId FROM ZipExtractionEntries WHERE ParentOperationId=$op ORDER BY CentralOrdinal";
        var items = new List<ZipEntryProgress>();
        using (var entries = await q.ExecuteReaderAsync(ct))
        {
            while (await entries.ReadAsync(ct))
            {
                if (items.Count >= ZipNumericLimits.Entries) throw new InvalidDataException("ZIP journal entry count exceeds profile.");
                var item = Decode<ZipEntryProgress>(entries.GetString(0));
                if (!Enum.IsDefined(item.State) || Json(item.Plan) != entries.GetString(1) ||
                    item.Plan.CentralOrdinal != entries.GetInt32(2) ||
                    item.Plan.FileOrdinal != (entries.IsDBNull(3) ? null : entries.GetInt32(3)) ||
                    item.Plan.ChildOperationId != (entries.IsDBNull(4) ? null : entries.GetString(4)) ||
                    item.Plan.ProvisionalArtifactId != (entries.IsDBNull(5) ? null : entries.GetString(5)))
                    throw new InvalidDataException("ZIP occurrence scalar binding disagrees with plan.");
                items.Add(item);
            }
        }
        if (result.Plan is { } plan && (plan.Hash != result.Fence.PlanHash || plan.Entries.Count != items.Count ||
            plan.Hash != PlanHash(plan.PreflightReceiptJson,plan.Entries) ||
            plan.Entries.Where((p,i) => Json(p) != Json(items[i].Plan)).Any()))
            throw new InvalidDataException("ZIP immutable plan disagrees with occurrences.");
        var fileCount=items.Count(e=>!e.Plan.IsDirectory);
        if (result.Fence.ConfirmedOrdinal < -1 || result.Fence.ConfirmedOrdinal >= fileCount ||
            (result.Plan is null && (items.Count != 0 || result.Fence.PlanHash is not null ||
                result.State is not (ZipParentState.AdmissionPending or ZipParentState.RequiresReview))) ||
            items.Any(e=>e.Plan.FileOrdinal is { } ordinal &&
                (ordinal<=result.Fence.ConfirmedOrdinal != (e.State is ZipEntryState.Acknowledged or ZipEntryState.Released))) ||
            (result.State is ZipParentState.Completed or ZipParentState.Released && result.Fence.ConfirmedOrdinal!=fileCount-1))
            throw new InvalidDataException("ZIP frontier contradicts durable state.");
        // Append-only reservations are authoritative for charged attempts. Mutable counters
        // cannot be reduced, even by a corrupted/repaired snapshot, to reset work allowance.
        q.CommandText="SELECT ReservationJson,ReservationId,Kind,FileOrdinal FROM ZipExtractionReservations WHERE ParentOperationId=$op ORDER BY rowid";
        var expected=new ZipBudget();var attempts=new Dictionary<(ZipWorkKind,int?),int>();
        using (var reservations=await q.ExecuteReaderAsync(ct))
        {
            while(await reservations.ReadAsync(ct))
            {
                var r=Decode<ZipWorkReservation>(reservations.GetString(0));
                if(!Enum.IsDefined(r.Kind)||r.ExpandedBytes<0||r.CompressedBytes<0 || r.Id!=reservations.GetString(1) ||
                    (int)r.Kind!=reservations.GetInt32(2) || r.FileOrdinal!=(reservations.IsDBNull(3)?null:reservations.GetInt32(3)))
                    throw new InvalidDataException("Invalid ZIP work evidence.");
                var e=r.FileOrdinal is null ? null : items.SingleOrDefault(e=>e.Plan.FileOrdinal==r.FileOrdinal);
                if(r.Kind==ZipWorkKind.Preflight)
                {
                    if(r.FileOrdinal is not null || r.ExpandedBytes!=result.Binding.Input.Length+65539 || r.CompressedBytes!=0)
                        throw new InvalidDataException("Invalid preflight work evidence.");
                }
                else if(r.FileOrdinal is null || e is null ||
                    (r.Kind==ZipWorkKind.Extraction && (r.ExpandedBytes!=e.Plan.ExpandedLength || r.CompressedBytes!=e.Plan.CompressedLength)) ||
                    (r.Kind==ZipWorkKind.Scanner && (r.ExpandedBytes!=e.Plan.ExpandedLength || r.CompressedBytes!=0)) ||
                    (r.Kind==ZipWorkKind.Ingestion && (r.ExpandedBytes!=0 || r.CompressedBytes!=0)))
                    throw new InvalidDataException("ZIP work evidence disagrees with occurrence.");
                var key=(r.Kind,r.FileOrdinal);attempts.TryGetValue(key,out var count);attempts[key]=count+1;
                if(count>=2)throw new InvalidDataException("ZIP work evidence exceeds attempt allowance.");
                expected=r.Kind switch
                {
                    ZipWorkKind.Preflight=>expected with{PreflightAttempts=checked(expected.PreflightAttempts+1),PreflightBytes=checked(expected.PreflightBytes+r.ExpandedBytes)},
                    ZipWorkKind.Extraction=>expected with{ExtractionAttempts=checked(expected.ExtractionAttempts+1),ExpandedReserved=checked(expected.ExpandedReserved+r.ExpandedBytes),
                        CompressedReserved=checked(expected.CompressedReserved+r.CompressedBytes),ProbeBytes=checked(expected.ProbeBytes+1),ReplayExpanded=checked(expected.ReplayExpanded+(count>0?r.ExpandedBytes:0))},
                    ZipWorkKind.Scanner=>expected with{ScannerAttempts=checked(expected.ScannerAttempts+1),ScannerReserved=checked(expected.ScannerReserved+r.ExpandedBytes),ReplayScanner=checked(expected.ReplayScanner+(count>0?r.ExpandedBytes:0))},
                    ZipWorkKind.Ingestion=>expected with{IngestionAttempts=checked(expected.IngestionAttempts+1)},
                    _=>throw new InvalidDataException("Unknown ZIP work evidence.")
                };
            }
        }
        expected=await ReconstructChargesAsync(c,tx,op,expected,result.Plan,items,result.Binding.Input,ct);
        await ValidateScansAsync(c,tx,result,items,ct);
        foreach(var entry in items.Where(e=>e.Ingestion is not null))
            await ValidateIngestionProofAsync(c,tx,result,entry,entry.Ingestion!,ct);
        await ValidateAcknowledgementsAsync(c,tx,result,items,ct);
        if(!parentEvidenceReviewOnly)await ValidateParentRetentionAsync(c,tx,result with{Entries=items},ct);
        expected=expected with{RejectedEntries=items.Count(e=>e.State==ZipEntryState.RequiresReview)};
        if(expected != result.Budget)
            throw new InvalidDataException("ZIP budget disagrees with nonrefundable reservations.");
        return result with { Entries = items };
    }
    public async Task<ZipParentSnapshot?> ReadAsync(string operationId, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(ct);
        using var tx = c.BeginTransaction(deferred: true);
        return await ReadAsync(c, tx, operationId, ct);
    }
    public async Task CreateAsync(ZipParentBinding binding, CancellationToken ct = default)
    {
        Validate(binding);
        await using var c = await OpenAsync(ct); using var tx = c.BeginTransaction(deferred: false);
        var old = await ReadAsync(c,tx,binding.OperationId,ct);
        if (old is not null)
        { if (Json(old.Binding) != Json(binding)) throw new InvalidDataException("ZIP parent binding changed."); return; }
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "INSERT INTO ZipExtractionParents VALUES($op,$binding,'',0,1,$until,0,NULL,NULL,-1,$budget,NULL)";
        q.Parameters.AddWithValue("$op",binding.OperationId); q.Parameters.AddWithValue("$binding",Json(binding));
        q.Parameters.AddWithValue("$until",DateTimeOffset.MinValue.ToString("O")); q.Parameters.AddWithValue("$budget",Json(new ZipBudget()));
        await q.ExecuteNonQueryAsync(ct); tx.Commit();
    }
    private static void Validate(ZipParentBinding b)
    {
        foreach (var id in new[] { b.OperationId,b.ParentArtifactId,b.Input.ContentId,b.Input.Revision })
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (b.Input.Length < 0 || b.Input.Length > ZipNumericLimits.Parent || b.Input.Sha256.Length != 64 || b.ProfileHash.Length != 64)
            throw new InvalidDataException("ZIP parent binding outside profile.");
        _ = Convert.FromHexString(b.Input.Sha256); _ = Convert.FromHexString(b.ProfileHash);
    }
    public async Task<ZipParentSnapshot> ClaimAsync(string operationId, string owner, TimeSpan duration, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        await using var c = await OpenAsync(ct); using var tx = c.BeginTransaction(deferred: false);
        var p = await ReadAsync(c,tx,operationId,ct) ?? throw new InvalidDataException("Missing ZIP parent.");
        if (p.OwnerUntil > _time.GetUtcNow() && p.Fence.Owner != owner) throw new ZipFenceException();
        var next = p with { Fence = p.Fence with { Owner=owner,Epoch=checked(p.Fence.Epoch+1),Revision=checked(p.Fence.Revision+1) },
            OwnerUntil = _time.GetUtcNow()+duration };
        await SaveAsync(c,tx,next,ct); tx.Commit(); return next;
    }
    private async Task<ZipParentSnapshot> FencedAsync(SqliteConnection c, SqliteTransaction tx, ZipFence f, CancellationToken ct)
    {
        var p = await ReadAsync(c,tx,f.OperationId,ct) ?? throw new ZipFenceException();
        if (p.Fence != f || p.OwnerUntil <= _time.GetUtcNow()) throw new ZipFenceException();
        return p;
    }
    private static async Task SaveAsync(SqliteConnection c, SqliteTransaction tx, ZipParentSnapshot p, CancellationToken ct)
    {
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "UPDATE ZipExtractionParents SET Owner=$owner,Epoch=$epoch,Revision=$rev,OwnerUntil=$until,State=$state,PlanJson=$plan,PlanHash=$hash,ConfirmedOrdinal=$frontier,BudgetJson=$budget WHERE OperationId=$op";
        q.Parameters.AddWithValue("$op",p.Binding.OperationId); q.Parameters.AddWithValue("$owner",p.Fence.Owner);
        q.Parameters.AddWithValue("$epoch",p.Fence.Epoch); q.Parameters.AddWithValue("$rev",p.Fence.Revision);
        q.Parameters.AddWithValue("$until",p.OwnerUntil.ToString("O")); q.Parameters.AddWithValue("$state",(int)p.State);
        q.Parameters.AddWithValue("$plan",p.Plan is null ? DBNull.Value : Json(p.Plan)); q.Parameters.AddWithValue("$hash",(object?)p.Fence.PlanHash ?? DBNull.Value);
        q.Parameters.AddWithValue("$frontier",p.Fence.ConfirmedOrdinal); q.Parameters.AddWithValue("$budget",Json(p.Budget));
        await q.ExecuteNonQueryAsync(ct);
    }
    public static string PlanHash(string receipt, IReadOnlyList<ZipEntryPlan> entries) =>
        ZipPlanBinding.Compute(receipt,entries);
    public async Task<ZipParentSnapshot> AdmitPlanAsync(ZipFence fence, ZipPlan plan, CancellationToken ct = default)
    {
        if (plan.Entries.Count > ZipNumericLimits.Entries || plan.Hash != PlanHash(plan.PreflightReceiptJson,plan.Entries))
            throw new InvalidDataException("Invalid ZIP plan.");
        long expanded=0,compressed=0; var files=0; var ids=new HashSet<string>();
        for (var i=0;i<plan.Entries.Count;i++)
        {
            var e=plan.Entries[i];
            if (e.CentralOrdinal != i || e.CompressedLength<0 || e.ExpandedLength<0) throw new InvalidDataException("Invalid ZIP occurrence.");
            if (e.IsDirectory)
            { if(e.FileOrdinal is not null || e.ChildOperationId is not null || e.ProvisionalArtifactId is not null) throw new InvalidDataException("Directory has child identity."); }
            else
            {
                if(e.FileOrdinal != files++ || string.IsNullOrWhiteSpace(e.ChildOperationId) || string.IsNullOrWhiteSpace(e.ProvisionalArtifactId) ||
                    !ids.Add(e.ChildOperationId) || !ids.Add(e.ProvisionalArtifactId) || e.ExpandedLength>ZipNumericLimits.Child)
                    throw new InvalidDataException("Invalid ZIP file identity or length.");
                expanded=checked(expanded+e.ExpandedLength); compressed=checked(compressed+e.CompressedLength);
            }
        }
        if(expanded>ZipNumericLimits.Aggregate || compressed>ZipNumericLimits.Parent) throw new InvalidDataException("ZIP aggregate outside profile.");
        await using var c=await OpenAsync(ct); using var tx=c.BeginTransaction(deferred:false);
        var p=await FencedAsync(c,tx,fence,ct);
        if(p.Plan is not null)
        {
            if(Json(p.Plan)!=Json(plan))throw new InvalidDataException("ZIP admitted plan cannot be replaced.");
            return p;
        }
        if(p.State!=ZipParentState.AdmissionPending) throw new InvalidOperationException("ZIP plan admission is not eligible.");
        if(p.Budget.PreflightAttempts==0)throw new InvalidDataException("ZIP preflight must be reserved before plan admission.");
        foreach(var e in plan.Entries)
        {
            using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="INSERT INTO ZipExtractionEntries VALUES($op,$central,$file,$child,$artifact,$plan,$progress)";
            q.Parameters.AddWithValue("$op",fence.OperationId);q.Parameters.AddWithValue("$central",e.CentralOrdinal);
            q.Parameters.AddWithValue("$file",(object?)e.FileOrdinal??DBNull.Value);q.Parameters.AddWithValue("$child",(object?)e.ChildOperationId??DBNull.Value);
            q.Parameters.AddWithValue("$artifact",(object?)e.ProvisionalArtifactId??DBNull.Value);q.Parameters.AddWithValue("$plan",Json(e));q.Parameters.AddWithValue("$progress",Json(new ZipEntryProgress(e)));
            await q.ExecuteNonQueryAsync(ct);
        }
        p=p with{ Plan=plan,State=ZipParentState.Planned,Fence=p.Fence with{PlanHash=plan.Hash,Revision=checked(p.Fence.Revision+1)},Entries=plan.Entries.Select(e=>new ZipEntryProgress(e)).ToArray() };
        await SaveAsync(c,tx,p,ct);tx.Commit();return p;
    }
    public async Task<ZipParentSnapshot> ReserveAsync(ZipFence fence, ZipWorkReservation r, CancellationToken ct=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(r.Id);
        if(r.Kind==ZipWorkKind.Scanner)throw new InvalidDataException("ZIP scanner reservations require atomic policy binding via BeginScanAsync.");
        if(r.ExpandedBytes<0 || r.CompressedBytes<0) throw new InvalidDataException("Negative ZIP reservation.");
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,fence,ct);
        using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText="SELECT ReservationJson FROM ZipExtractionReservations WHERE ParentOperationId=$op AND ReservationId=$id";
        q.Parameters.AddWithValue("$op",fence.OperationId);q.Parameters.AddWithValue("$id",r.Id);
        if(await q.ExecuteScalarAsync(ct) is string existing)
        { if(existing!=Json(r))throw new InvalidDataException("ZIP reservation identity reused.");return p; }
        var b=p.Budget;
        if (!Enum.IsDefined(r.Kind)) throw new InvalidDataException("Unknown ZIP work kind.");
        if(r.Kind==ZipWorkKind.Preflight)
        {
            if(p.State!=ZipParentState.AdmissionPending || r.FileOrdinal is not null || r.CompressedBytes!=0 || r.ExpandedBytes!=p.Binding.Input.Length+65539)
                throw new InvalidDataException("Invalid preflight reservation.");
            b=b with{PreflightAttempts=checked(b.PreflightAttempts+1),PreflightBytes=checked(b.PreflightBytes+r.ExpandedBytes)};
            if(b.PreflightAttempts>2)throw new InvalidDataException("ZIP preflight attempts exhausted.");
        }
        else
        {
            if(p.State is not (ZipParentState.Planned or ZipParentState.Processing) || r.FileOrdinal!=fence.ConfirmedOrdinal+1)
                throw new ZipFenceException();
            var entry=p.Entries.SingleOrDefault(e=>e.Plan.FileOrdinal==r.FileOrdinal)??throw new InvalidDataException("Missing ZIP file.");
            q.CommandText="SELECT COUNT(*) FROM ZipExtractionReservations WHERE ParentOperationId=$op AND FileOrdinal=$file AND Kind=$kind";
            q.Parameters.AddWithValue("$file",r.FileOrdinal!.Value);q.Parameters.AddWithValue("$kind",(int)r.Kind);
            var count=Convert.ToInt32(await q.ExecuteScalarAsync(ct));if(count>=2)throw new InvalidDataException("ZIP attempt allowance exhausted.");
            switch(r.Kind)
            {
                case ZipWorkKind.Extraction:
                    if(entry.State!=ZipEntryState.Planned||entry.Plan.IsEncrypted||r.ExpandedBytes!=entry.Plan.ExpandedLength || r.CompressedBytes!=entry.Plan.CompressedLength)throw new InvalidDataException("Extraction reservation binding changed.");
                    b=b with{ExtractionAttempts=checked(b.ExtractionAttempts+1),ExpandedReserved=checked(b.ExpandedReserved+r.ExpandedBytes),CompressedReserved=checked(b.CompressedReserved+r.CompressedBytes),
                        ProbeBytes=checked(b.ProbeBytes+1),ReplayExpanded=checked(b.ReplayExpanded+(count>0?r.ExpandedBytes:0))};break;
                case ZipWorkKind.Scanner:
                    if(entry.State!=ZipEntryState.Materialized || r.ExpandedBytes!=entry.Plan.ExpandedLength || r.CompressedBytes!=0)throw new InvalidDataException("Scanner reservation binding changed.");
                    b=b with{ScannerAttempts=checked(b.ScannerAttempts+1),ScannerReserved=checked(b.ScannerReserved+r.ExpandedBytes),ReplayScanner=checked(b.ReplayScanner+(count>0?r.ExpandedBytes:0))};break;
                case ZipWorkKind.Ingestion:
                    if(entry.State is not (ZipEntryState.Scanned or ZipEntryState.Ingested) || r.ExpandedBytes!=0 || r.CompressedBytes!=0)throw new InvalidDataException("Ingestion not admitted.");
                    b=b with{IngestionAttempts=checked(b.IngestionAttempts+1)};break;
                default:throw new InvalidDataException("Unknown ZIP work.");
            }
        }
        ValidateBudget(b);
        q.Parameters.Clear();q.CommandText="INSERT INTO ZipExtractionReservations VALUES($op,$id,$file,$kind,$json)";
        q.Parameters.AddWithValue("$op",fence.OperationId);q.Parameters.AddWithValue("$id",r.Id);q.Parameters.AddWithValue("$file",(object?)r.FileOrdinal??DBNull.Value);
        q.Parameters.AddWithValue("$kind",(int)r.Kind);q.Parameters.AddWithValue("$json",Json(r));await q.ExecuteNonQueryAsync(ct);
        p=p with{Budget=b,Fence=p.Fence with{Revision=checked(p.Fence.Revision+1)}};
        await SaveAsync(c,tx,p,ct);tx.Commit();return p;
    }
    private static void ValidateBudget(ZipBudget b)
    {
        if(new[]{b.PreflightAttempts,b.PreflightBytes,b.ExtractionAttempts,b.ExpandedReserved,b.ExpandedProduced,
            b.ReplayExpanded,b.CompressedReserved,b.ParentReadBytes,b.ProbeBytes,b.CrcBytes,b.ScannerAttempts,
            b.ScannerReserved,b.ReplayScanner,b.IngestionAttempts,b.RejectedEntries}.Any(v=>v<0) ||
            b.PreflightAttempts>2 || b.PreflightBytes>2*(ZipNumericLimits.Parent+65539) ||
            b.ExpandedProduced>ZipNumericLimits.ExpandedWork || b.ParentReadBytes>ZipNumericLimits.ParentReads ||
            b.CrcBytes>ZipNumericLimits.ExpandedWork || b.RejectedEntries>ZipNumericLimits.Entries ||
            b.ExpandedReserved>ZipNumericLimits.ExpandedWork || b.CompressedReserved>ZipNumericLimits.CompressedWork ||
            b.ReplayExpanded>ZipNumericLimits.Aggregate || b.ScannerReserved>ZipNumericLimits.ExpandedWork || b.ReplayScanner>ZipNumericLimits.Aggregate ||
            b.ExtractionAttempts>2000 || b.ScannerAttempts>2000 || b.IngestionAttempts>2000 || b.ProbeBytes>2000)
            throw new InvalidDataException("ZIP durable work budget exhausted.");
    }
    public async Task<ZipParentSnapshot> ReviewAsync(ZipFence fence,string safeReason,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(safeReason)||safeReason.Length>128||safeReason.Any(char.IsControl))throw new ArgumentException("Safe typed reason required.");
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var p=await FencedAsync(c,tx,fence,ct);
        p=p with{State=ZipParentState.RequiresReview,Fence=p.Fence with{Revision=checked(p.Fence.Revision+1)}};
        await SaveAsync(c,tx,p,ct);using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="UPDATE ZipExtractionParents SET SafeReason=$reason WHERE OperationId=$op";
        q.Parameters.AddWithValue("$reason",safeReason);q.Parameters.AddWithValue("$op",fence.OperationId);await q.ExecuteNonQueryAsync(ct);tx.Commit();return p;
    }
}
