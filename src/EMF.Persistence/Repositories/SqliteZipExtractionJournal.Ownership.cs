using EMF.Core.Contracts.Zip;

namespace EMF.Persistence.Repositories;

public sealed partial class SqliteZipExtractionJournal
{
    private async Task AdmissionOwnershipEligibleAsync(Microsoft.Data.Sqlite.SqliteConnection c,
        Microsoft.Data.Sqlite.SqliteTransaction tx, ZipParentSnapshot parent, CancellationToken ct)
    {
        // Absence preserves explicit pre-v5 compatibility APIs without inventing
        // approval. Presence can veto concurrency ownership, never grant authority.
        var admission = await ReadAdmissionCoreAsync(c, tx, parent.Binding.OperationId, ct);
        if (admission is not null && (admission.State != ZipAdmissionState.ParentBound ||
            admission.Parent != parent.Binding || admission.LatestAuthorityEvidence is null ||
            admission.LatestAuthorityEvidence.ExpiresUtc <= _time.GetUtcNow()))
            throw new UnauthorizedAccessException("ZIP admission does not permit execution ownership.");
    }
    public async Task<DateTimeOffset> RenewOwnershipAsync(string operationId, string owner, long epoch, TimeSpan duration, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (epoch < 1 || duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        await using var c = await OpenAsync(ct); using var tx = c.BeginTransaction(deferred: false);
        var p = await ReadAsync(c, tx, operationId, ct) ?? throw new ZipFenceException();
        var now = _time.GetUtcNow();
        if (p.Fence.Owner != owner || p.Fence.Epoch != epoch || p.OwnerUntil <= now ||
            p.State == ZipParentState.RequiresReview) throw new ZipFenceException();
        if (p.State == ZipParentState.Released)
        {
            var admission = await ReadAdmissionCoreAsync(c, tx, operationId, ct);
            if (admission is not null && (admission.State is not (ZipAdmissionState.ParentBound or ZipAdmissionState.Completed) ||
                admission.Parent != p.Binding || admission.LatestAuthorityEvidence is null ||
                admission.LatestAuthorityEvidence.ExpiresUtc <= now)) throw new UnauthorizedAccessException("ZIP authority stopped.");
            throw new ZipOwnershipReleasedException();
        }
        await AdmissionOwnershipEligibleAsync(c, tx, p, ct);
        var until = now + duration;
        if (until < p.OwnerUntil) until = p.OwnerUntil;
        using var q = c.CreateCommand(); q.Transaction = tx;
        q.CommandText = "UPDATE ZipExtractionParents SET OwnerUntil=$until WHERE OperationId=$op";
        q.Parameters.AddWithValue("$until", until.ToString("O")); q.Parameters.AddWithValue("$op", operationId);
        await q.ExecuteNonQueryAsync(ct); ct.ThrowIfCancellationRequested();
        if (p.OwnerUntil <= _time.GetUtcNow()) throw new ZipFenceException();
        tx.Commit(); return until;
    }
}
