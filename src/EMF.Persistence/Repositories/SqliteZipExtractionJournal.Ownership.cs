using EMF.Core.Contracts.Zip;

namespace EMF.Persistence.Repositories;

public sealed partial class SqliteZipExtractionJournal
{
    public async Task<ZipParentSnapshot> ClaimOwnershipAsync(ZipOwnershipClaimRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request); request.Validate(); ct.ThrowIfCancellationRequested();
        await using var c = await OpenAsync(ct); using var tx = c.BeginTransaction(deferred: false);
        var admission = await ReadAdmissionCoreAsync(c, tx, request.ExpectedAdmission.OperationId, ct)
            ?? throw new InvalidDataException("Missing original ZIP admission.");
        var parent = await ReadAsync(c, tx, admission.OperationId, ct)
            ?? throw new InvalidDataException("Missing ZIP parent.");
        if (admission != request.ExpectedAdmission || parent.Fence != request.ExpectedFence ||
            parent.OwnerUntil != request.ExpectedOwnerUntil) throw new ZipFenceException();
        await AdmissionOwnershipEligibleAsync(c, tx, parent, ct);
        var retained = await ParentRetentionAsync(c, tx, admission.OperationId, ct)
            ?? throw new InvalidDataException("Missing admitted ZIP parent retention.");
        ParentBinding(parent, retained);
        if (retained.Identity.Source != admission.Source || retained.Identity.NamespaceId != admission.Binding.ParentNamespaceId)
            throw new InvalidDataException("ZIP ownership retention differs from admission.");
        using var profile = System.Text.Json.JsonDocument.Parse(admission.Binding.ProfileJson);
        if (!profile.RootElement.TryGetProperty("ownershipLeaseTicks", out var lease) || !lease.TryGetInt64(out var ticks) ||
            ticks <= 0 || request.Duration.Ticks > ticks)
            throw new InvalidDataException("ZIP ownership lease exceeds admitted profile.");
        var capability = request.Purpose == ZipOwnershipPurpose.InitialExecution
            ? ZipAuthorityCapabilities.Admit : ZipAuthorityCapabilities.Recover;
        if (admission.Approval is null || admission.LatestAuthorityEvidence is null ||
            (admission.Approval.Capabilities & capability) == 0 ||
            (admission.LatestAuthorityEvidence.Capabilities & capability) == 0)
            throw new UnauthorizedAccessException("Recorded ZIP authority does not cover the claim purpose.");
        var now = _time.GetUtcNow();
        if (request.Purpose == ZipOwnershipPurpose.InitialExecution)
        {
            if (parent.Fence.Epoch != 0 || parent.Fence.Revision != 1 || parent.Fence.Owner != "" ||
                parent.OwnerUntil > now || parent.State != ZipParentState.AdmissionPending ||
                parent.Plan is not null || parent.Fence.PlanHash is not null || parent.Fence.ConfirmedOrdinal != -1 ||
                parent.Entries.Count != 0 || parent.Budget != new ZipBudget())
                throw new UnauthorizedAccessException("ZIP initial ownership requires a fresh parent.");
        }
        else if (parent.Fence.Epoch <= 0 || string.IsNullOrWhiteSpace(parent.Fence.Owner) ||
            parent.State is not (ZipParentState.AdmissionPending or ZipParentState.Planned or ZipParentState.Processing or ZipParentState.Completed))
            throw new UnauthorizedAccessException("ZIP recovery requires eligible prior ownership.");
        if (parent.OwnerUntil > now && parent.Fence.Owner != request.Owner) throw new ZipFenceException();
        var next = parent with
        {
            Fence = parent.Fence with { Owner = request.Owner, Epoch = checked(parent.Fence.Epoch + 1), Revision = checked(parent.Fence.Revision + 1) },
            OwnerUntil = now + request.Duration
        };
        await SaveAsync(c, tx, next, ct);
        ct.ThrowIfCancellationRequested();
        if (next.OwnerUntil <= _time.GetUtcNow()) throw new ZipFenceException();
        if (admission.LatestAuthorityEvidence.ExpiresUtc <= _time.GetUtcNow())
            throw new UnauthorizedAccessException("Recorded ZIP authority expired during the claim.");
        tx.Commit(); return next;
    }

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
