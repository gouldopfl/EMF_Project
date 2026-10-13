using System.Text.Json;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed partial class ZipExecutionAttempt
{
    private int childIngestionInvocationUsed;
    internal void EnterChildIngestionInvocation()
    {
        if (Interlocked.Exchange(ref childIngestionInvocationUsed, 1) != 0)
            throw new UnauthorizedAccessException("A new child ingestion invocation requires a fresh execution attempt.");
    }

    internal ZipPhaseIIBoundary<ZipChildIngestionEffectRequest> IngestionBoundary(
        Func<ZipParentSnapshot> current, int ordinal, ZipChildIngestionEffect effect)
        => Boundary(() =>
        {
            var p = current(); Validate(p);
            return new ZipChildIngestionEffectRequest(admission, p,
                p.Entries.Single(e => e.Plan.FileOrdinal == ordinal).Plan, effect);
        }, (g, r, ct) => g.AuthorizeChildIngestionEffectAsync(r, ct));

    internal async Task<ZipChildIngestionRuntime> OpenChildAsync(ZipParentSnapshot parent,
        ZipEntryProgress entry, IZipChildIngestionEffectGate gate, CancellationToken ct)
    {
        await RevalidateAsync(ct); Validate(parent);
        await using var scope = await gate.OpenAsync(ZipChildIngestionEffect.OpenChild, ct);
        Task<ZipChildIngestionRuntime> pending;
        using (scope.Accept(ct)) pending = root.OpenChildAsync(parent, entry, gate, ct);
        var runtime = await pending;
        if (!ReferenceEquals(runtime.EffectGate, gate))
            throw new UnauthorizedAccessException("Child runtime did not retain the delegated authority.");
        await scope.FinishAsync(ct);
        return runtime;
    }
}

internal sealed class ZipChildIngestionEffectGate(ZipExecutionAttempt attempt,
    IZipExtractionJournal parents, Func<ZipParentSnapshot> current, int ordinal) : IZipChildIngestionEffectGate
{
    public TimeProvider Clock => attempt.Clock;
    public async Task<IZipChildIngestionEffectScope> OpenAsync(ZipChildIngestionEffect effect,
        CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, attempt.Invalidated);
        ct = linked.Token;
        await attempt.RevalidateAsync(ct);
        var expected = current(); attempt.Validate(expected);
        var actual = await parents.ReadAsync(expected.Binding.OperationId, ct) ?? throw new ZipFenceException();
        if (actual.Fence != expected.Fence || actual.Binding != expected.Binding ||
            actual.State is not (ZipParentState.Planned or ZipParentState.Processing) ||
            ordinal != actual.Fence.ConfirmedOrdinal + 1)
            throw new ZipFenceException();
        var entry = actual.Entries.SingleOrDefault(e => e.Plan.FileOrdinal == ordinal)
            ?? throw new InvalidDataException("Missing admitted child occurrence.");
        var prior = expected.Entries.SingleOrDefault(e => e.Plan.FileOrdinal == ordinal);
        if (prior is null || prior.Plan != entry.Plan || prior.Retained != entry.Retained ||
            prior.EvidenceJson != entry.EvidenceJson || entry.State is not (ZipEntryState.Scanned or ZipEntryState.Ingested))
            throw new InvalidDataException("Child ingestion evidence changed.");
        var scan = JsonSerializer.Deserialize<MalwareScanEvidence>(entry.EvidenceJson ?? "")
            ?? throw new InvalidDataException("Missing Clean scan evidence.");
        MalwareScanValidation.Evidence(scan);
        if (!scan.IsClean || entry.Retained is null ||
            scan.Request.Content != new MalwareScanContent(entry.Retained.ContentId,
                entry.Retained.Revision, entry.Retained.Sha256, entry.Retained.Length))
            throw new InvalidDataException("Child ingestion requires exact Clean evidence.");
        var boundary = attempt.IngestionBoundary(current, ordinal, effect);
        try { await boundary.RevalidateAsync(ct); return new Scope(boundary); }
        catch { await boundary.DisposeAsync(); throw; }
    }

    private sealed class Scope(ZipPhaseIIBoundary<ZipChildIngestionEffectRequest> boundary)
        : IZipChildIngestionEffectScope
    {
        public CancellationToken Invalidated => boundary.Invalidated;
        public ValueTask RevalidateAsync(CancellationToken ct = default) => boundary.RevalidateAsync(ct);
        public IDisposable Accept(CancellationToken ct = default) => boundary.Accept(ct);
        public Task FinishAsync(CancellationToken ct = default) => boundary.FinishAsync(ct);
        public ValueTask DisposeAsync() => boundary.DisposeAsync();
    }
}
