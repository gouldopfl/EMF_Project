using System.Security.Cryptography;
using System.Text;
using EMF.Core.Contracts.Zip;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Repositories;

public sealed partial class SqliteZipExtractionJournal
{
    private static void AdmissionKey(ZipAdmissionKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ZipAdmissionValidation.Text(key.IssuerId); ZipAdmissionValidation.Text(key.RequestId);
    }

    private static ZipParentAdmission ApplyAdmissionEvent(ZipParentAdmission p, ZipAdmissionEvent e)
    {
        ZipAdmissionValidation.Text(e.EventId); ZipAdmissionValidation.Text(e.ExecutingActorId, 256);
        if (!Enum.IsDefined(e.Kind) || e.OccurredUtc < p.CreatedUtc)
            throw new InvalidDataException("Invalid ZIP authority event.");
        var stops = e.Kind is ZipAdmissionEventKind.Denied or ZipAdmissionEventKind.Revoked or ZipAdmissionEventKind.RequiresReview;
        if (e.Approval is not null && e.Kind is not (ZipAdmissionEventKind.Approved or ZipAdmissionEventKind.AuthorityValidated) ||
            e.Source is not null && e.Kind != ZipAdmissionEventKind.SourceBound ||
            e.Parent is not null && e.Kind != ZipAdmissionEventKind.ParentBound ||
            e.SafeReason is not null && !stops)
            throw new InvalidDataException("ZIP event carries unrelated evidence.");
        if (p.State is ZipAdmissionState.Completed or ZipAdmissionState.Denied or ZipAdmissionState.Revoked or ZipAdmissionState.RequiresReview)
            throw new InvalidDataException("Terminal ZIP admission cannot be reopened.");
        switch (e.Kind)
        {
            case ZipAdmissionEventKind.Approved:
            case ZipAdmissionEventKind.AuthorityValidated:
                var a = e.Approval ?? throw new InvalidDataException("Missing issuer approval.");
                foreach (var id in new[] { a.IssuerId, a.DecisionId, a.AuthorityRevision, a.PolicyVersion }) ZipAdmissionValidation.Text(id);
                ZipAdmissionValidation.Hash(a.BindingHash);
                if (a.ParentOperationId != p.OperationId ||
                    a.IssuerId != p.Key.IssuerId || a.BindingHash != ZipAdmissionValidation.BindingHash(p.Binding) ||
                    (a.Capabilities & ZipAuthorityCapabilities.Admit) == 0 ||
                    (a.Capabilities & ~(ZipAuthorityCapabilities.Admit | ZipAuthorityCapabilities.Recover |
                        ZipAuthorityCapabilities.Review | ZipAuthorityCapabilities.DelegateChildren | ZipAuthorityCapabilities.Cleanup)) != 0 ||
                    a.IssuedUtc > e.OccurredUtc || a.ExpiresUtc <= e.OccurredUtc)
                    throw new InvalidDataException("Issuer approval does not bind the original ZIP admission.");
                if (e.Kind == ZipAdmissionEventKind.Approved)
                {
                    if (p.State != ZipAdmissionState.ApprovalPending || e.ExecutingActorId != p.Binding.OriginalActorId)
                        throw new InvalidDataException("Original ZIP approval actor/state changed.");
                    return p with { Revision = checked(p.Revision + 1), State = ZipAdmissionState.Approved, Approval = a, LatestAuthorityEvidence = a };
                }
                if (p.Approval is null || a.AuthorityRevision != p.Approval.AuthorityRevision ||
                    a.PolicyVersion != p.Approval.PolicyVersion || a.Capabilities != p.Approval.Capabilities ||
                    (a.Capabilities & ZipAuthorityCapabilities.Recover) == 0)
                    throw new InvalidDataException("ZIP recovery cannot replace original authority semantics.");
                return p with { Revision = checked(p.Revision + 1), LatestAuthorityEvidence = a };
            case ZipAdmissionEventKind.SourceBound:
                var source = e.Source ?? throw new InvalidDataException("Missing exact source binding.");
                ZipAdmissionValidation.Hash(source.Sha256);
                if (p.State != ZipAdmissionState.Approved || source.ContentId != p.Binding.SourceContentId ||
                    source.Revision != p.Binding.SourceRevision || source.Length < 0 || source.Length > ZipNumericLimits.Parent)
                    throw new InvalidDataException("ZIP source differs from approved revision.");
                return p with { Revision = checked(p.Revision + 1), State = ZipAdmissionState.SourceBound, Source = source };
            case ZipAdmissionEventKind.ParentBound:
                var parent = e.Parent ?? throw new InvalidDataException("Missing retained parent binding.");
                if (parent.Input is null || p.State != ZipAdmissionState.SourceBound || parent.OperationId != p.OperationId ||
                    parent.ParentArtifactId != p.Binding.ParentArtifactId || parent.ProfileHash != p.Binding.ProfileHash ||
                    parent.Input.Sha256 != p.Source!.Sha256 || parent.Input.Length != p.Source.Length)
                    throw new InvalidDataException("Retained ZIP parent differs from admission.");
                ZipAdmissionValidation.Text(parent.Input.ContentId); ZipAdmissionValidation.Text(parent.Input.Revision);
                return p with { Revision = checked(p.Revision + 1), State = ZipAdmissionState.ParentBound, Parent = parent };
            case ZipAdmissionEventKind.Completed:
                if (p.State != ZipAdmissionState.ParentBound) throw new InvalidDataException("ZIP parent has not been bound.");
                return p with { Revision = checked(p.Revision + 1), State = ZipAdmissionState.Completed };
            default:
                ZipAdmissionValidation.Text(e.SafeReason);
                if (e.Kind == ZipAdmissionEventKind.Denied && p.State != ZipAdmissionState.ApprovalPending ||
                    e.Kind == ZipAdmissionEventKind.Revoked && p.Approval is null)
                    throw new InvalidDataException("Invalid ZIP authority denial/revocation transition.");
                return p with { Revision = checked(p.Revision + 1), SafeReason = e.SafeReason,
                    State = e.Kind == ZipAdmissionEventKind.Denied ? ZipAdmissionState.Denied :
                        e.Kind == ZipAdmissionEventKind.Revoked ? ZipAdmissionState.Revoked : ZipAdmissionState.RequiresReview };
        }
    }

    private static async Task<ZipParentAdmission?> ReadAdmissionCoreAsync(SqliteConnection c, SqliteTransaction tx,
        string operation, CancellationToken ct)
    {
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT IssuerId,RequestId,BindingJson,BindingHash,CreatedUtc FROM ZipParentAdmissions WHERE OperationId=$op";
        q.Parameters.AddWithValue("$op", operation);
        ZipParentAdmission p;
        using (var row = await q.ExecuteReaderAsync(ct))
        {
            if (!await row.ReadAsync(ct)) return null;
            var binding = Decode<ZipAdmissionBinding>(row.GetString(2));
            ZipAdmissionValidation.Binding(binding);
            if (ZipAdmissionValidation.BindingHash(binding) != row.GetString(3))
                throw new InvalidDataException("ZIP admission binding changed.");
            p = new(operation, new(row.GetString(0), row.GetString(1)), binding, DateTimeOffset.Parse(row.GetString(4)));
            AdmissionKey(p.Key); ZipAdmissionValidation.Text(operation);
        }
        q.CommandText = "SELECT Sequence,EventId,EvidenceJson FROM ZipParentAdmissionEvents WHERE OperationId=$op ORDER BY Sequence";
        using var events = await q.ExecuteReaderAsync(ct);
        while (await events.ReadAsync(ct))
        {
            var e = Decode<ZipAdmissionEvent>(events.GetString(2));
            if (events.GetInt64(0) != p.Revision + 1 || events.GetString(1) != e.EventId)
                throw new InvalidDataException("ZIP admission event sequence changed.");
            p = ApplyAdmissionEvent(p, e);
        }
        return p;
    }

    public async Task<ZipParentAdmission> ResolveOrReserveAdmissionAsync(ZipAdmissionKey key, ZipAdmissionBinding binding, CancellationToken ct = default)
    {
        AdmissionKey(key); ZipAdmissionValidation.Binding(binding);
        await using var c = await OpenAsync(ct); using var tx = c.BeginTransaction(deferred: false);
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT OperationId FROM ZipParentAdmissions WHERE IssuerId=$issuer AND RequestId=$request";
        q.Parameters.AddWithValue("$issuer", key.IssuerId); q.Parameters.AddWithValue("$request", key.RequestId);
        var existing = await q.ExecuteScalarAsync(ct) as string;
        if (existing is not null)
        {
            var p = (await ReadAdmissionCoreAsync(c, tx, existing, ct))!;
            if (p.Binding != binding) throw new InvalidDataException("Original ZIP admission cannot switch source, actor, lineage or profile.");
            return p;
        }
        var operation = Guid.NewGuid().ToString("N"); var created = _time.GetUtcNow();
        q.CommandText = "INSERT INTO ZipParentAdmissions VALUES($op,$issuer,$request,$binding,$hash,$created)";
        q.Parameters.AddWithValue("$op", operation); q.Parameters.AddWithValue("$binding", Json(binding));
        q.Parameters.AddWithValue("$hash", ZipAdmissionValidation.BindingHash(binding)); q.Parameters.AddWithValue("$created", created.ToString("O"));
        await q.ExecuteNonQueryAsync(ct); ct.ThrowIfCancellationRequested(); tx.Commit();
        return new(operation, key, binding, created);
    }

    public async Task<ZipParentAdmission?> ReadAdmissionAsync(string operationId, CancellationToken ct = default)
    {
        ZipAdmissionValidation.Text(operationId);
        await using var c = await OpenAsync(ct); using var tx = c.BeginTransaction(deferred: true);
        return await ReadAdmissionCoreAsync(c, tx, operationId, ct);
    }

    public async Task<ZipParentAdmission?> ReadAdmissionByKeyAsync(ZipAdmissionKey key, CancellationToken ct = default)
    {
        AdmissionKey(key);
        await using var c = await OpenAsync(ct); using var tx = c.BeginTransaction(deferred: true);
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT OperationId FROM ZipParentAdmissions WHERE IssuerId=$issuer AND RequestId=$request";
        q.Parameters.AddWithValue("$issuer", key.IssuerId); q.Parameters.AddWithValue("$request", key.RequestId);
        var operation = await q.ExecuteScalarAsync(ct) as string;
        return operation is null ? null : await ReadAdmissionCoreAsync(c, tx, operation, ct);
    }

    public async Task<IReadOnlyList<ZipParentAdmission>> ReadRecoveryAdmissionsAsync(string? afterOperationId, int limit, CancellationToken ct = default)
    {
        if (limit is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(limit));
        if (afterOperationId is not null) ZipAdmissionValidation.Text(afterOperationId);
        await using var c = await OpenAsync(ct); using var tx = c.BeginTransaction(deferred: true);
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT OperationId FROM ZipParentAdmissions WHERE OperationId>$after ORDER BY OperationId LIMIT $limit";
        q.Parameters.AddWithValue("$after", afterOperationId ?? ""); q.Parameters.AddWithValue("$limit", limit);
        var operations = new List<string>();
        using (var rows = await q.ExecuteReaderAsync(ct)) while (await rows.ReadAsync(ct)) operations.Add(rows.GetString(0));
        var result = new List<ZipParentAdmission>();
        foreach (var operation in operations) result.Add((await ReadAdmissionCoreAsync(c, tx, operation, ct))!);
        return result;
    }

    public async Task<ZipParentAdmission> AppendAdmissionEventAsync(ZipParentAdmission expected, ZipAdmissionEvent evidence, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(evidence);
        await using var c = await OpenAsync(ct); using var tx = c.BeginTransaction(deferred: false);
        var p = await ReadAdmissionCoreAsync(c, tx, expected.OperationId, ct) ?? throw new InvalidDataException("Missing original ZIP admission.");
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "SELECT OperationId,EvidenceJson FROM ZipParentAdmissionEvents WHERE EventId=$event";
        q.Parameters.AddWithValue("$event", evidence.EventId);
        using (var row = await q.ExecuteReaderAsync(ct))
        {
            if (await row.ReadAsync(ct))
            {
                if (row.GetString(0) != p.OperationId || row.GetString(1) != Json(evidence) || p.Key != expected.Key || p.Binding != expected.Binding)
                    throw new InvalidDataException("ZIP authority event identity changed.");
                return p;
            }
        }
        if (p != expected) throw new ZipFenceException();
        if (evidence.OccurredUtc > _time.GetUtcNow()) throw new InvalidDataException("ZIP authority event is in the future.");
        var next = ApplyAdmissionEvent(p, evidence);
        if (evidence.Kind is ZipAdmissionEventKind.Approved or ZipAdmissionEventKind.AuthorityValidated or ZipAdmissionEventKind.SourceBound or ZipAdmissionEventKind.ParentBound or ZipAdmissionEventKind.Completed)
        {
            if (next.LatestAuthorityEvidence!.ExpiresUtc <= _time.GetUtcNow()) throw new UnauthorizedAccessException("Recorded ZIP approval expired; issuer revalidation is required.");
        }
        if (evidence.Kind is ZipAdmissionEventKind.ParentBound or ZipAdmissionEventKind.Completed)
        {
            var parent = await ReadAsync(c, tx, p.OperationId, ct) ?? throw new InvalidDataException("Missing retained ZIP parent.");
            var retained = await ParentRetentionAsync(c, tx, p.OperationId, ct) ?? throw new InvalidDataException("Missing original ZIP retention.");
            if (parent.Binding != next.Parent || retained.Identity.Source != next.Source ||
                retained.Identity.NamespaceId != p.Binding.ParentNamespaceId)
                throw new InvalidDataException("ZIP admission/retention/journal binding differs.");
            if (evidence.Kind == ZipAdmissionEventKind.Completed && parent.State != ZipParentState.Released)
                throw new InvalidDataException("ZIP operation has not completed release.");
        }
        q.CommandText = "INSERT INTO ZipParentAdmissionEvents VALUES($op,$sequence,$event,$evidence)";
        q.Parameters.AddWithValue("$op", p.OperationId); q.Parameters.AddWithValue("$sequence", next.Revision);
        q.Parameters.AddWithValue("$evidence", Json(evidence));
        await q.ExecuteNonQueryAsync(ct); ct.ThrowIfCancellationRequested(); tx.Commit(); return next;
    }

    public async Task<IDisposable> AcquireAdmissionExecutionAsync(string operationId, CancellationToken ct = default)
    {
        if (await ReadAdmissionAsync(operationId, ct) is null) throw new InvalidDataException("Missing ZIP admission.");
        var root = _path + ".zip-admission-execution";
        if (new DirectoryInfo(root).LinkTarget is not null) throw new IOException("ZIP admission execution contains a symbolic link.");
        _platform.CreatePrivateDirectory(root); _platform.ValidatePrivatePermissions(root);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operationId)));
        return await _platform.AcquireAsync(Path.Combine(root, name), true, ct);
    }
}
