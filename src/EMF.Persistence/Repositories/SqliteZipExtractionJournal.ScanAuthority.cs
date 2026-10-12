using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Repositories;

public sealed partial class SqliteZipExtractionJournal
{
    internal Func<string,Task>? ScanCheckpoint { get; set; }
    private Task ScanAt(string name) => ScanCheckpoint?.Invoke(name) ?? Task.CompletedTask;

    private static async Task<ZipFrozenScan> LatestScanCoreAsync(SqliteConnection c, SqliteTransaction tx,
        string operation, int ordinal, CancellationToken ct)
    {
        using var q=c.CreateCommand();q.Transaction=tx;
        q.Parameters.AddWithValue("$op",operation);q.Parameters.AddWithValue("$file",ordinal);
        q.CommandText="SELECT BindingJson,EvidenceJson FROM ZipExtractionScans WHERE ParentOperationId=$op AND FileOrdinal=$file ORDER BY rowid DESC LIMIT 1";
        ZipScanAttempt? attempt=null;MalwareScanEvidence? evidence=null;
        using(var r=await q.ExecuteReaderAsync(ct))
            if(await r.ReadAsync(ct)){attempt=Decode<ZipScanAttempt>(r.GetString(0));if(!r.IsDBNull(1))evidence=Decode<MalwareScanEvidence>(r.GetString(1));}
        q.CommandText="SELECT COUNT(*) FROM ZipExtractionReservations WHERE ParentOperationId=$op AND FileOrdinal=$file AND Kind=2";
        return new(attempt,evidence,Convert.ToInt32(await q.ExecuteScalarAsync(ct)));
    }
    public async Task<ZipFrozenScan> ReadLatestScanAsync(ZipFence fence,int fileOrdinal,CancellationToken ct=default)
    {
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction();
        var p=await FencedAsync(c,tx,fence,ct);
        if(fileOrdinal!=p.Fence.ConfirmedOrdinal+1)throw new ZipFenceException();
        return await LatestScanCoreAsync(c,tx,fence.OperationId,fileOrdinal,ct);
    }
    private static async Task<ZipEntryProgress> ValidateScannerRetentionAsync(SqliteConnection c,SqliteTransaction tx,
        ZipParentSnapshot p,ZipScanAttempt attempt,CancellationToken ct)
    {
        if(attempt.FileOrdinal!=p.Fence.ConfirmedOrdinal+1||p.State is not (ZipParentState.Planned or ZipParentState.Processing))
            throw new ZipFenceException();
        var entry=p.Entries.Single(e=>e.Plan.FileOrdinal==attempt.FileOrdinal);
        if(entry.Retained is null||attempt.PlanHash!=p.Fence.PlanHash||attempt.Request.Content!=ScanContent(entry.Retained))
            throw new InvalidDataException("Scanner retained binding changed.");
        var retention=await RetentionAsync(c,tx,p.Binding.OperationId,attempt.FileOrdinal,ct);
        if(retention?.State!=ZipRetentionState.Created||retention.CreateReceipt?.CurrentRevision is null||
            retention.Identity.ObjectId!=entry.Retained.ContentId||retention.CreateReceipt.CurrentRevision.Value.Value!=entry.Retained.Revision||
            retention.Identity.PlaintextSha256!=entry.Retained.Sha256||retention.Identity.Length!=entry.Retained.Length)
            throw new InvalidDataException("Scanner retained receipt changed.");
        return entry;
    }
    private async Task ValidateScannerNamespaceAsync(SqliteConnection c,SqliteTransaction tx,ZipParentSnapshot parent,int ordinal,CancellationToken ct)
    {
        var admission=await ReadAdmissionCoreAsync(c,tx,parent.Binding.OperationId,ct)
            ??throw new UnauthorizedAccessException("Scanner requires admitted namespace binding.");
        var child=await RetentionAsync(c,tx,parent.Binding.OperationId,ordinal,ct);
        if(child?.Identity.NamespaceId!=admission.Binding.ChildNamespaceId)
            throw new InvalidDataException("Scanner child namespace differs from admission.");
    }
    private async Task ValidateRecordingAnchorsAsync(SqliteConnection c,SqliteTransaction tx,ZipParentSnapshot parent,
        IZipScanObservationGuard guard,CancellationToken ct)
    {
        var current=await ReadAdmissionCoreAsync(c,tx,parent.Binding.OperationId,ct)
            ??throw new UnauthorizedAccessException("Recording requires original admission anchors.");
        var expected=guard.Admission;
        // Terminal/expired execution evidence does not erase admitted factual
        // recording; immutable identity still cannot be changed or repaired.
        if(current.OperationId!=expected.OperationId||current.Key!=expected.Key||current.Binding!=expected.Binding||
            current.Approval!=expected.Approval||current.Source!=expected.Source||current.Parent!=expected.Parent||
            guard.Parent!=parent.Binding||expected.Parent!=parent.Binding)
            throw new UnauthorizedAccessException("Recording admission identity changed.");
        var retained=await ParentRetentionAsync(c,tx,parent.Binding.OperationId,ct)
            ??throw new InvalidDataException("Recording parent retention missing.");
        ParentBinding(parent,retained);
        if(current.Source!=retained.Identity.Source||current.Binding.ParentNamespaceId!=retained.Identity.NamespaceId)
            throw new InvalidDataException("Recording retained parent identity changed.");
        var child=await RetentionAsync(c,tx,parent.Binding.OperationId,guard.Observation.Invocation.Attempt.FileOrdinal,ct);
        if(child?.Identity.NamespaceId!=current.Binding.ChildNamespaceId)
            throw new InvalidDataException("Recording retained child namespace changed.");
    }
    public async Task<DateTimeOffset> ValidateScanReadAsync(ZipFence fence,ZipScanAttempt attempt,CancellationToken ct=default)
    {
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction();
        var p=await FencedAsync(c,tx,fence,ct);
        var until=await ValidateExecutionAdmissionAsync(c,tx,p,ct);
        var entry=await ValidateScannerRetentionAsync(c,tx,p,attempt,ct);
        await ValidateScannerNamespaceAsync(c,tx,p,attempt.FileOrdinal,ct);
        var latest=await LatestScanCoreAsync(c,tx,fence.OperationId,attempt.FileOrdinal,ct);
        if(entry.State!=ZipEntryState.Materialized||latest.Attempt!=attempt||latest.Evidence is not null)
            throw new InvalidDataException("Scanner read has no exact eligible pending attempt.");
        return until;
    }
    public async Task<ZipParentSnapshot> RecordScanObservationAsync(ZipFence fence,IZipScanObservationGuard guard,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        await ValidateEffectAsync(guard,ct);
        var observed=guard.Observation;
        if(guard.ExpectedFence!=fence||!observed.Invocation.Owns(observed))throw new UnauthorizedAccessException("Wrong observation provenance/fence.");
        var attempt=observed.Invocation.Attempt;var evidence=observed.Evidence;
        MalwareScanValidation.Evidence(evidence);
        if(evidence.Request!=attempt.Request)throw new InvalidDataException("Observation request changed.");
        await ScanAt("BeforeRecordTransaction");
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        await ValidateEffectAsync(guard,ct);
        var p=await FencedAsync(c,tx,fence,ct);
        await ValidateRecordingAnchorsAsync(c,tx,p,guard,ct);
        var entry=await ValidateScannerRetentionAsync(c,tx,p,attempt,ct);
        await ValidateScannerNamespaceAsync(c,tx,p,attempt.FileOrdinal,ct);
        if(entry.State!=ZipEntryState.Materialized)throw new InvalidDataException("Observation requires materialized child.");
        var latest=await LatestScanCoreAsync(c,tx,fence.OperationId,attempt.FileOrdinal,ct);
        if(latest.Attempt!=attempt)throw new ZipFenceException();
        var json=Json(evidence);
        if(latest.Evidence is not null)
        {
            if(Json(latest.Evidence)!=json)throw new InvalidDataException("Observation is already frozen.");
            // No stale/expired/idempotent authorization shortcut.
            await CommitEffectAsync(tx,guard,ct,p.OwnerUntil);return p;
        }
        using var q=c.CreateCommand();q.Transaction=tx;
        q.Parameters.AddWithValue("$op",fence.OperationId);q.Parameters.AddWithValue("$id",attempt.ReservationId);
        q.Parameters.AddWithValue("$file",attempt.FileOrdinal);q.Parameters.AddWithValue("$evidence",json);
        q.Parameters.AddWithValue("$progress",Json(entry with{EvidenceJson=json}));
        q.CommandText="UPDATE ZipExtractionScans SET EvidenceJson=$evidence WHERE ParentOperationId=$op AND ReservationId=$id; UPDATE ZipExtractionEntries SET ProgressJson=$progress WHERE ParentOperationId=$op AND FileOrdinal=$file";
        await q.ExecuteNonQueryAsync(ct);
        p=p with{Fence=fence with{Revision=checked(fence.Revision+1)}};
        await SaveAsync(c,tx,p,ct);await ScanAt("BeforeRecordCommit");
        await CommitEffectAsync(tx,guard,ct,p.OwnerUntil);
        return p with{Entries=p.Entries.Select(e=>e.Plan.FileOrdinal==attempt.FileOrdinal?e with{EvidenceJson=json}:e).ToArray()};
    }
    public async Task<ZipParentSnapshot> AdvanceRecordedCleanAsync(ZipFence fence,ZipScanAttempt attempt,
        MalwareScanEvidence evidence,IZipEffectBoundaryGuard guard,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(guard);MalwareScanValidation.Evidence(evidence);
        if(!evidence.IsClean||evidence.Request!=attempt.Request)throw new InvalidDataException("Only exact validated Clean observation can progress.");
        await ValidateEffectAsync(guard,ct);
        await using var c=await OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        await ValidateEffectAsync(guard,ct);var p=await FencedAsync(c,tx,fence,ct);
        await ValidateExecutionAdmissionAsync(c,tx,p,ct);
        var entry=await ValidateScannerRetentionAsync(c,tx,p,attempt,ct);
        await ValidateScannerNamespaceAsync(c,tx,p,attempt.FileOrdinal,ct);
        var latest=await LatestScanCoreAsync(c,tx,fence.OperationId,attempt.FileOrdinal,ct);
        if(latest.Attempt!=attempt||latest.Evidence is null||Json(latest.Evidence)!=Json(evidence)||entry.EvidenceJson!=Json(evidence))
            throw new InvalidDataException("Clean progression has no exact latest frozen observation.");
        if(entry.State==ZipEntryState.Scanned)
        {await CommitEffectAsync(tx,guard,ct,p.OwnerUntil,admissionEligibility:()=>ValidateExecutionAdmissionAsync(c,tx,p,ct));return p;}
        if(entry.State!=ZipEntryState.Materialized)throw new InvalidDataException("Clean progression is ineligible.");
        var next=entry with{State=ZipEntryState.Scanned,SafeReason=null};
        using var q=c.CreateCommand();q.Transaction=tx;
        q.Parameters.AddWithValue("$op",fence.OperationId);q.Parameters.AddWithValue("$file",attempt.FileOrdinal);q.Parameters.AddWithValue("$progress",Json(next));
        q.CommandText="UPDATE ZipExtractionEntries SET ProgressJson=$progress WHERE ParentOperationId=$op AND FileOrdinal=$file";
        await q.ExecuteNonQueryAsync(ct);p=p with{Fence=fence with{Revision=checked(fence.Revision+1)}};
        await SaveAsync(c,tx,p,ct);await ScanAt("BeforeProgressionCommit");
        await CommitEffectAsync(tx,guard,ct,p.OwnerUntil,admissionEligibility:()=>ValidateExecutionAdmissionAsync(c,tx,p,ct));
        return p with{Entries=p.Entries.Select(e=>e.Plan.FileOrdinal==attempt.FileOrdinal?next:e).ToArray()};
    }
}
