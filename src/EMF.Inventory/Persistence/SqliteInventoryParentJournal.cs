using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Inventory.Contracts;
using EMF.Inventory.Models;
using EMF.Inventory.Storage;
using Microsoft.Data.Sqlite;

namespace EMF.Inventory.Persistence;

public sealed class SqliteInventoryParentJournal : IInventoryParentJournal
{
    private readonly string _path;
    private readonly InventoryProcessingLimits _limits;
    public SqliteInventoryParentJournal(string path, InventoryProcessingLimits? limits = null)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        _limits = limits ?? new(); _limits.Validate(); _path = Path.GetFullPath(path);
        _ = new LinuxInventorySnapshotWorkspace(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path)) { using var f = new FileStream(_path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }); f.Flush(true); LinuxInventorySnapshotWorkspace.FlushDirectory(Path.GetDirectoryName(_path)!); }
        using var c = Open(); using var tx = c.BeginTransaction(deferred: false); using var cmd = Cmd(c, tx, "PRAGMA user_version");
        int version = Convert.ToInt32(cmd.ExecuteScalar()); if (version is not (0 or 1 or 2)) throw new InvalidDataException("Unknown Inventory journal schema.");
        if (version == 0) { cmd.CommandText = """
            CREATE TABLE InventoryParents(Id TEXT PRIMARY KEY,Hash TEXT NOT NULL,Header TEXT NOT NULL,Owner TEXT NOT NULL,Epoch INTEGER NOT NULL,Version INTEGER NOT NULL,Frontier INTEGER NOT NULL,Status INTEGER NOT NULL);
            CREATE TABLE InventoryPlanItems(Parent TEXT NOT NULL,Ordinal INTEGER NOT NULL,Child TEXT NOT NULL UNIQUE,Plan TEXT NOT NULL,State INTEGER NOT NULL,PRIMARY KEY(Parent,Ordinal));
            CREATE TABLE InventoryConfirmations(Parent TEXT NOT NULL,Ordinal INTEGER NOT NULL,Evidence TEXT NOT NULL,PRIMARY KEY(Parent,Ordinal));
            CREATE TABLE InventoryRetention(Id TEXT PRIMARY KEY,Parent TEXT NOT NULL,Payload TEXT NOT NULL);
            CREATE TRIGGER InventoryImmutableItems BEFORE UPDATE OF Parent,Ordinal,Child,Plan ON InventoryPlanItems BEGIN SELECT RAISE(ABORT,'Immutable Inventory plan'); END;
            CREATE TRIGGER InventoryImmutableHeader BEFORE UPDATE OF Hash,Header ON InventoryParents BEGIN SELECT RAISE(ABORT,'Immutable Inventory binding'); END;
            PRAGMA user_version=1;
            """; cmd.ExecuteNonQuery(); }
        // Version 1 journals retain all evidence. Add an indexed parent range without
        // rewriting rows; initialization is also safe to repeat on version 2 journals.
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS InventoryRetentionParentId ON InventoryRetention(Parent,Id); PRAGMA user_version=2;";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }
    private SqliteConnection Open()
    {
        LinuxInventorySnapshotWorkspace.Validate(_path, false);
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5 }.ToString()); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA synchronous=FULL"; cmd.ExecuteNonQuery(); return c;
    }
    private SqliteCommand Cmd(SqliteConnection c, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    { var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql; if (sql.Contains("$jsonLimit", StringComparison.Ordinal)) cmd.Parameters.AddWithValue("$jsonLimit", _limits.MaximumPlanJsonBytes); foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value); return cmd; }
    private string Encode<T>(T value) { var j = JsonSerializer.Serialize(value); if (Encoding.UTF8.GetByteCount(j) > _limits.MaximumPlanJsonBytes) throw new InvalidDataException("Journal size limit."); return j; }
    private T Decode<T>(string j) { if (Encoding.UTF8.GetByteCount(j) > _limits.MaximumPlanJsonBytes) throw new InvalidDataException("Journal size limit."); return JsonSerializer.Deserialize<T>(j) ?? throw new InvalidDataException(); }
    private string Hash(InventoryParentPlan p) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Encode(p))));
    private void Binding(InventoryRetainedBinding b)
    {
        foreach (var v in new[] { b.ObjectId, b.OwnerToken, b.Revision, b.CreateOperationId, b.ReleaseOperationId }) InventoryIdentity.Validate(v);
        if (b.Representation != "inventory-sqlite-v1" || b.Length <= 0 || b.Length > _limits.MaximumSnapshotBytes || b.Fingerprint.Length != 64 || b.Fingerprint.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Invalid retained binding.");
    }
    private void Plan(InventoryParentPlan p)
    {
        foreach (var id in new[] { p.ParentId, p.WorkflowId, p.WorkflowOperationId }) InventoryIdentity.Validate(id);
        if (!Enum.IsDefined(p.Mode) || p.Items.Count > _limits.MaximumPlanItems || p.Mode == InventoryMode.ProtectedContent && p.Authority is null || p.Mode == InventoryMode.MetadataOnly && p.Authority is not null) throw new InvalidDataException("Invalid Inventory admission.");
        if (p.Authority is { } a) foreach (var id in new[] { a.AuthorityId, a.Revision, a.ActorId }) InventoryIdentity.Validate(id);
        var children = new HashSet<string>(); var artifacts = new HashSet<string>(); var objects = new HashSet<string>(); long size = 0; string? last = null;
        for (int i = 0; i < p.Items.Count; i++)
        {
            var item = p.Items[i]; InventoryIdentity.Validate(item.ChildOperationId); InventoryIdentity.Validate(item.ArtifactId); Binding(item.Retained);
            if (item.Ordinal != i || !children.Add(item.ChildOperationId) || !artifacts.Add(item.ArtifactId) || !objects.Add(item.Retained.ObjectId) || !Path.IsPathFullyQualified(item.SourceLocator) || item.SourceLocator.Length > _limits.MaximumStringCharacters || last is not null && string.CompareOrdinal(last, item.SourceLocator) >= 0 || Encoding.UTF8.GetByteCount(item.DraftJson) > 16384) throw new InvalidDataException("Invalid Inventory item.");
            size = checked(size + item.Retained.Length); last = item.SourceLocator; if (size > _limits.MaximumRetainedBytes) throw new InvalidDataException("Retained plan budget exhausted.");
        }
    }
    public async Task<InventoryParentState> AdmitAsync(InventoryParentPlan plan, string ownerToken, CancellationToken ct = default)
    {
        Plan(plan); InventoryIdentity.Validate(ownerToken); var hash = Hash(plan); using var c = Open(); using var tx = c.BeginTransaction(deferred: false);
        using (var known = Cmd(c, tx, "SELECT Hash FROM InventoryParents WHERE Id=$id", ("$id", plan.ParentId))) { var value = await known.ExecuteScalarAsync(ct); if (value is not null) { if ((string)value != hash) throw new InvalidOperationException("Conflicting Inventory admission."); tx.Commit(); return (await LoadAsync(plan.ParentId, ct))!; } }
        foreach (var item in plan.Items) { using var read = Cmd(c, tx, "SELECT CASE WHEN length(CAST(Payload AS BLOB))<=$jsonLimit THEN Payload ELSE NULL END FROM InventoryRetention WHERE Id=$id", ("$id", item.Retained.ObjectId)); var json = await read.ExecuteScalarAsync(ct) as string; var r = json is null ? null : Decode<InventoryRetentionRecord>(json); if (r?.ParentId != plan.ParentId || r.Status != InventoryRetentionStatus.Sealed || r.Binding != item.Retained) throw new InvalidDataException("Unsealed admitted input."); }
        using (var insert = Cmd(c, tx, "INSERT INTO InventoryParents VALUES($id,$hash,$header,$owner,1,1,-1,0)", ("$id", plan.ParentId), ("$hash", hash), ("$header", Encode(plan with { Items = Array.Empty<InventoryPlanItem>() })), ("$owner", ownerToken))) await insert.ExecuteNonQueryAsync(ct);
        foreach (var item in plan.Items) { using var insert = Cmd(c, tx, "INSERT INTO InventoryPlanItems VALUES($id,$n,$child,$plan,0)", ("$id", plan.ParentId), ("$n", item.Ordinal), ("$child", item.ChildOperationId), ("$plan", Encode(item))); await insert.ExecuteNonQueryAsync(ct); }
        tx.Commit(); return (await LoadAsync(plan.ParentId, ct))!;
    }
    public async Task<InventoryParentState?> LoadAsync(string parentId, CancellationToken ct = default)
    {
        InventoryIdentity.Validate(parentId); using var c = Open(); using var tx = c.BeginTransaction(deferred: true);
        InventoryParentPlan plan; string owner, hash; long epoch, version; int frontier; InventoryParentStatus status;
        using (var cmd = Cmd(c, tx, "SELECT CASE WHEN length(CAST(Header AS BLOB))<=$jsonLimit THEN Header ELSE NULL END,Owner,Epoch,Version,Frontier,Status,Hash FROM InventoryParents WHERE Id=$id", ("$id", parentId))) using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null; if (r.IsDBNull(0)) throw new InvalidDataException("Parent header byte limit."); plan = Decode<InventoryParentPlan>(r.GetString(0)); owner = r.GetString(1); epoch = r.GetInt64(2); version = r.GetInt64(3); frontier = r.GetInt32(4); status = (InventoryParentStatus)r.GetInt32(5); hash = r.GetString(6);
        }
        var items = new List<InventoryPlanItem>(); long loadedBytes = Encoding.UTF8.GetByteCount(Encode(plan));
        using (var cmd = Cmd(c, tx, "SELECT length(CAST(Plan AS BLOB)),CASE WHEN length(CAST(Plan AS BLOB))<=$jsonLimit THEN Plan ELSE NULL END FROM InventoryPlanItems WHERE Parent=$id ORDER BY Ordinal LIMIT $limit", ("$id", parentId), ("$limit", _limits.MaximumPlanItems + 1)))
        using (var r = await cmd.ExecuteReaderAsync(ct)) while (await r.ReadAsync(ct))
            {
                loadedBytes = checked(loadedBytes + r.GetInt64(0));
                if (items.Count >= _limits.MaximumPlanItems || loadedBytes > _limits.MaximumPlanJsonBytes || r.IsDBNull(1)) throw new InvalidDataException("Loaded Inventory plan byte/item limit.");
                items.Add(Decode<InventoryPlanItem>(r.GetString(1)));
            }
        plan = plan with { Items = items.ToArray() }; Plan(plan); _ = Encode(plan); if (Hash(plan) != hash || frontier < -1 || frontier >= items.Count || epoch < 1 || version < 1 || !Enum.IsDefined(status)) throw new InvalidDataException("Damaged Inventory parent.");
        if (status == InventoryParentStatus.Completed && frontier != items.Count - 1) throw new InvalidDataException("Incomplete completed parent.");
        var proofCount = 0;
        using (var cmd = Cmd(c, tx, "SELECT C.Ordinal,CASE WHEN length(CAST(C.Evidence AS BLOB))<=$jsonLimit THEN C.Evidence ELSE NULL END,P.State FROM InventoryConfirmations C JOIN InventoryPlanItems P ON P.Parent=C.Parent AND P.Ordinal=C.Ordinal WHERE C.Parent=$id ORDER BY C.Ordinal LIMIT $limit", ("$id", parentId), ("$limit", _limits.MaximumPlanItems + 1)))
        using (var r = await cmd.ExecuteReaderAsync(ct)) while (await r.ReadAsync(ct))
            {
                var n = r.GetInt32(0); if (n != proofCount || n > frontier || r.GetInt32(2) != 4) throw new InvalidDataException("Confirmation frontier mismatch.");
                var p = Decode<InventoryChildConfirmation>(r.GetString(1));
                if (p.Ordinal != n || p.ChildOperationId != items[n].ChildOperationId || string.IsNullOrEmpty(p.Evidence) || p.Evidence.Length > 4096 || !Enum.IsDefined(p.Disposition) ||
                   (plan.Mode == InventoryMode.MetadataOnly) != (p.Disposition == InventoryConfirmationDisposition.MetadataPersisted)) throw new InvalidDataException("Confirmation binding mismatch.");
                InventoryIdentity.Validate(p.CanonicalArtifactId); proofCount++;
            }
        if (proofCount != frontier + 1) throw new InvalidDataException("Missing durable confirmation evidence.");
        using (var cmd = Cmd(c, tx, "SELECT COUNT(*) FROM InventoryPlanItems WHERE Parent=$id AND ((Ordinal<=$f AND State<>4) OR (Ordinal>$f AND State=4))", ("$id", parentId), ("$f", frontier))) if (Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) != 0) throw new InvalidDataException("Confirmed child state mismatch.");
        tx.Commit(); return new(plan, owner, epoch, version, frontier, status);
    }
    private void Check(SqliteConnection c, SqliteTransaction tx, InventoryParentState e, bool active = true)
    {
        Plan(e.Plan); using var cmd = Cmd(c, tx, "SELECT Hash,Owner,Epoch,Version,Frontier,Status FROM InventoryParents WHERE Id=$id", ("$id", e.Plan.ParentId));
        using var r = cmd.ExecuteReader(); if (!r.Read() || r.GetString(0) != Hash(e.Plan)) throw new InvalidOperationException("Inventory plan binding conflict.");
        if (r.GetString(1) != e.OwnerToken || r.GetInt64(2) != e.OwnerEpoch) throw new InvalidOperationException("Stale Inventory worker ownership.");
        if (r.GetInt64(3) != e.Version) throw new InvalidOperationException("Stale Inventory journal version.");
        if (r.GetInt32(4) != e.ConfirmedOrdinal || (active && r.GetInt32(5) != 0)) throw new InvalidOperationException("Inventory parent state conflict.");
    }
    public async Task<InventoryParentState> TakeOwnershipAsync(InventoryParentState e, string ownerToken, CancellationToken ct = default)
    { InventoryIdentity.Validate(ownerToken); using var c = Open(); using var tx = c.BeginTransaction(deferred: false); Check(c, tx, e); using var cmd = Cmd(c, tx, "UPDATE InventoryParents SET Owner=$owner,Epoch=$epoch,Version=$v WHERE Id=$id", ("$owner", ownerToken), ("$epoch", checked(e.OwnerEpoch + 1)), ("$v", checked(e.Version + 1)), ("$id", e.Plan.ParentId)); await cmd.ExecuteNonQueryAsync(ct); tx.Commit(); return (await LoadAsync(e.Plan.ParentId, ct))!; }
    public async Task StartNextAsync(InventoryParentState e, int ordinal, CancellationToken ct = default)
    { if (ordinal != e.ConfirmedOrdinal + 1 || ordinal >= e.Plan.Items.Count) throw new InvalidOperationException("Only next child may start."); using var c = Open(); using var tx = c.BeginTransaction(deferred: false); Check(c, tx, e); using var cmd = Cmd(c, tx, "UPDATE InventoryPlanItems SET State=1 WHERE Parent=$id AND Ordinal=$n AND State IN(0,1,2)", ("$id", e.Plan.ParentId), ("$n", ordinal)); if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Child terminal."); tx.Commit(); }
    public async Task<InventoryParentState> ConfirmNextAsync(InventoryParentState e, InventoryChildConfirmation proof, CancellationToken ct = default)
    {
        if (proof.Ordinal != e.ConfirmedOrdinal + 1 || proof.Ordinal >= e.Plan.Items.Count || proof.ChildOperationId != e.Plan.Items[proof.Ordinal].ChildOperationId || !Enum.IsDefined(proof.Disposition) || string.IsNullOrEmpty(proof.Evidence) || proof.Evidence.Length > 4096 || e.Plan.Mode == InventoryMode.MetadataOnly && proof.Disposition != InventoryConfirmationDisposition.MetadataPersisted || e.Plan.Mode == InventoryMode.ProtectedContent && proof.Disposition == InventoryConfirmationDisposition.MetadataPersisted) throw new InvalidOperationException("Invalid confirmation disposition/binding."); InventoryIdentity.Validate(proof.CanonicalArtifactId);
        using var c = Open(); using var tx = c.BeginTransaction(deferred: false); Check(c, tx, e);
        using (var cmd = Cmd(c, tx, "UPDATE InventoryPlanItems SET State=4 WHERE Parent=$id AND Ordinal=$n AND State=1", ("$id", e.Plan.ParentId), ("$n", proof.Ordinal))) if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Child not started.");
        using (var cmd = Cmd(c, tx, "INSERT INTO InventoryConfirmations VALUES($id,$n,$proof)", ("$id", e.Plan.ParentId), ("$n", proof.Ordinal), ("$proof", Encode(proof)))) await cmd.ExecuteNonQueryAsync(ct);
        using (var cmd = Cmd(c, tx, "UPDATE InventoryParents SET Frontier=$n,Version=$v WHERE Id=$id", ("$id", e.Plan.ParentId), ("$n", proof.Ordinal), ("$v", checked(e.Version + 1)))) await cmd.ExecuteNonQueryAsync(ct); tx.Commit(); return (await LoadAsync(e.Plan.ParentId, ct))!;
    }
    public async Task<InventoryParentState> CompleteAsync(InventoryParentState e, CancellationToken ct = default)
    { if (e.ConfirmedOrdinal != e.Plan.Items.Count - 1) throw new InvalidOperationException("Unconfirmed children remain."); using var c = Open(); using var tx = c.BeginTransaction(deferred: false); Check(c, tx, e); using var cmd = Cmd(c, tx, "UPDATE InventoryParents SET Status=1,Version=$v WHERE Id=$id", ("$id", e.Plan.ParentId), ("$v", checked(e.Version + 1))); await cmd.ExecuteNonQueryAsync(ct); tx.Commit(); return (await LoadAsync(e.Plan.ParentId, ct))!; }
    public async Task MarkChildFailureAsync(InventoryParentState e, int ordinal, bool terminal, CancellationToken ct = default)
    { if (ordinal != e.ConfirmedOrdinal + 1) throw new InvalidOperationException(); using var c = Open(); using var tx = c.BeginTransaction(deferred: false); Check(c, tx, e); using var cmd = Cmd(c, tx, "UPDATE InventoryPlanItems SET State=$s WHERE Parent=$id AND Ordinal=$n AND State=1", ("$s", terminal ? 3 : 2), ("$id", e.Plan.ParentId), ("$n", ordinal)); if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException(); if (terminal) { using var stop = Cmd(c, tx, "UPDATE InventoryParents SET Status=2,Version=$v WHERE Id=$id", ("$id", e.Plan.ParentId), ("$v", checked(e.Version + 1))); await stop.ExecuteNonQueryAsync(ct); } tx.Commit(); }
    // The bound includes released/history rows: one parent may never reserve more
    // identities than its immutable plan cap. LIMIT bounds even damaged old journals;
    // INDEXED BY requires a parent range seek rather than a historical table scan.
    private async Task<int> RetentionCountAsync(SqliteConnection c, SqliteTransaction? tx, string parentId, CancellationToken ct)
    {
        using var cmd = Cmd(c, tx, "SELECT Id FROM InventoryRetention INDEXED BY InventoryRetentionParentId WHERE Parent=$parent ORDER BY Id LIMIT $limit", ("$parent", parentId), ("$limit", _limits.MaximumPlanItems + 1));
        using var reader = await cmd.ExecuteReaderAsync(ct); var count = 0;
        while (await reader.ReadAsync(ct))
            if (++count > _limits.MaximumPlanItems) throw new InvalidDataException("Retention item limit.");
        return count;
    }
    public async Task<bool> HasRetainedPreparationAsync(string parentId, CancellationToken ct = default)
    { InventoryIdentity.Validate(parentId); using var c = Open(); return await RetentionCountAsync(c, null, parentId, ct) != 0; }
    public async Task ReserveRetentionAsync(InventoryRetentionRecord r, CancellationToken ct = default)
    { foreach (var id in new[] { r.ParentId, r.ObjectId, r.OwnerToken, r.CreateOperationId, r.ReleaseOperationId }) InventoryIdentity.Validate(id); if (r.Status != InventoryRetentionStatus.Reserved || r.Binding is not null) throw new InvalidDataException(); using var c = Open(); using var tx = c.BeginTransaction(deferred: false); if (await RetentionCountAsync(c, tx, r.ParentId, ct) >= _limits.MaximumPlanItems) throw new InvalidDataException("Retention item budget."); using var cmd = Cmd(c, tx, "INSERT INTO InventoryRetention VALUES($id,$parent,$payload)", ("$id", r.ObjectId), ("$parent", r.ParentId), ("$payload", Encode(r))); await cmd.ExecuteNonQueryAsync(ct); tx.Commit(); }
    public async Task<InventoryRetentionRecord?> ReadRetentionAsync(string objectId, CancellationToken ct = default)
    { InventoryIdentity.Validate(objectId); using var c = Open(); using var cmd = Cmd(c, null, "SELECT CASE WHEN length(CAST(Payload AS BLOB))<=$jsonLimit THEN Payload ELSE NULL END FROM InventoryRetention WHERE Id=$id", ("$id", objectId)); var value = await cmd.ExecuteScalarAsync(ct); if (value is DBNull) throw new InvalidDataException("Retained journal size limit."); var json = value as string; return json is null ? null : Decode<InventoryRetentionRecord>(json); }
    public async Task UpdateRetentionAsync(InventoryRetentionRecord e, InventoryRetentionRecord n, CancellationToken ct = default)
    {
        if (e.ParentId != n.ParentId || e.ObjectId != n.ObjectId || e.OwnerToken != n.OwnerToken || e.CreateOperationId != n.CreateOperationId || e.ReleaseOperationId != n.ReleaseOperationId || e.Fingerprint is not null && (e.Fingerprint != n.Fingerprint || e.Length != n.Length) || e.Binding is not null && e.Binding != n.Binding ||
            (e.Status, n.Status) is not ((InventoryRetentionStatus.Reserved, InventoryRetentionStatus.Sealing) or (InventoryRetentionStatus.Sealing, InventoryRetentionStatus.Sealed) or (InventoryRetentionStatus.ReleasePending, InventoryRetentionStatus.Released))) throw new InvalidOperationException("Invalid retained transition.");
        if (n.Status == InventoryRetentionStatus.Sealed) { if (n.Binding is null) throw new InvalidDataException(); Binding(n.Binding); if (n.Binding.Fingerprint != n.Fingerprint || n.Binding.Length != n.Length || n.Binding.ObjectId != n.ObjectId || n.Binding.OwnerToken != n.OwnerToken || n.Binding.CreateOperationId != n.CreateOperationId || n.Binding.ReleaseOperationId != n.ReleaseOperationId) throw new InvalidDataException(); }
        using var c = Open(); using var tx = c.BeginTransaction(deferred: false);
        if (n.Status == InventoryRetentionStatus.Sealing)
        {
            if (n.Length <= 0 || n.Length > _limits.MaximumSnapshotBytes) throw new InvalidDataException("Retention snapshot byte limit.");
            long total = n.Length;
            using var rows = Cmd(c, tx, "SELECT Id,CASE WHEN length(CAST(Payload AS BLOB))<=$jsonLimit THEN Payload ELSE NULL END FROM InventoryRetention INDEXED BY InventoryRetentionParentId WHERE Parent=$parent ORDER BY Id LIMIT $limit", ("$parent", n.ParentId), ("$limit", _limits.MaximumPlanItems + 1));
            using var reader = await rows.ExecuteReaderAsync(ct); var count = 0;
            while (await reader.ReadAsync(ct))
            {
                if (++count > _limits.MaximumPlanItems) throw new InvalidDataException("Retention item limit.");
                if (reader.IsDBNull(1)) throw new InvalidDataException("Retained journal size limit.");
                var other = Decode<InventoryRetentionRecord>(reader.GetString(1));
                if (other.ParentId != n.ParentId || other.ObjectId != reader.GetString(0) || !Enum.IsDefined(other.Status) || other.Length < 0 || other.Length > _limits.MaximumSnapshotBytes)
                    throw new InvalidDataException("Damaged retained journal binding.");
                if (other.ObjectId != n.ObjectId && other.Status != InventoryRetentionStatus.Released) total = checked(total + other.Length);
            }
            if (total > _limits.MaximumRetainedBytes) throw new InvalidDataException("Retention aggregate byte budget exhausted.");
        }
        using var cmd = Cmd(c, tx, "UPDATE InventoryRetention SET Payload=$next WHERE Id=$id AND Payload=$expected", ("$id", e.ObjectId), ("$next", Encode(n)), ("$expected", Encode(e))); if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Retained state conflict."); tx.Commit();
    }
    public async Task AuthorizeReleaseAsync(InventoryParentState e, int ordinal, CancellationToken ct = default)
    {
        if (ordinal < 0 || ordinal > e.ConfirmedOrdinal) throw new InvalidOperationException("Unconfirmed input cannot release."); using var c = Open(); using var tx = c.BeginTransaction(deferred: false); Check(c, tx, e, false);
        using var read = Cmd(c, tx, "SELECT CASE WHEN length(CAST(Payload AS BLOB))<=$jsonLimit THEN Payload ELSE NULL END FROM InventoryRetention WHERE Id=$id", ("$id", e.Plan.Items[ordinal].Retained.ObjectId)); var r = Decode<InventoryRetentionRecord>((string)(await read.ExecuteScalarAsync(ct) ?? throw new InvalidDataException())); if (r.Binding != e.Plan.Items[ordinal].Retained) throw new InvalidDataException();
        if (r.Status == InventoryRetentionStatus.Sealed) { using var cmd = Cmd(c, tx, "UPDATE InventoryRetention SET Payload=$next WHERE Id=$id", ("$id", r.ObjectId), ("$next", Encode(r with { Status = InventoryRetentionStatus.ReleasePending }))); await cmd.ExecuteNonQueryAsync(ct); } else if (r.Status is not (InventoryRetentionStatus.ReleasePending or InventoryRetentionStatus.Released)) throw new InvalidOperationException(); tx.Commit();
    }
}
