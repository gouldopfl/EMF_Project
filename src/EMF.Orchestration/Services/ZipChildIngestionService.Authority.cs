using System.Text.Json;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public enum ZipChildIngestionStop { Ingested, OutsideChildIngestionBoundary }
public sealed record ZipChildIngestionResult(ZipChildIngestionStop Stop, ZipParentSnapshot Parent, int? FileOrdinal = null);

public sealed partial class ZipChildIngestionService
{
    public async Task<ZipChildIngestionResult> IngestNextAsync(ZipParentSnapshot supplied,
        ZipExecutionAttempt attempt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        retention.ValidateScanDependencies();
        if (host is not IZipGuardedChildIngestionHost guardedHost)
            throw new NotSupportedException("Guarded ZIP child ingestion host is required.");
        guardedHost.ValidateGuardedDependencies();
        attempt.EnterChildIngestionInvocation();
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(ct, attempt.Invalidated);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(execution.Token);
        deadline.CancelAfter(ZipNumericLimits.AttemptTimeout);
        ct = deadline.Token;
        await attempt.RevalidateAsync(ct);
        await ZipChildWorkGate.Gate.WaitAsync(ct);
        try
        {
            var parent = await parents.ReadAsync(supplied.Binding.OperationId, ct) ?? throw new ZipFenceException();
            if (parent.Fence != supplied.Fence || parent.Binding != supplied.Binding) throw new ZipFenceException();
            attempt.Validate(parent);
            if (parent.State is not (ZipParentState.Planned or ZipParentState.Processing))
                return await Stop(ZipChildIngestionStop.OutsideChildIngestionBoundary, null);
            var ordinal = checked(parent.Fence.ConfirmedOrdinal + 1);
            var entry = parent.Entries.SingleOrDefault(e => e.Plan.FileOrdinal == ordinal);
            if (entry is null || entry.State is not (ZipEntryState.Scanned or ZipEntryState.Ingested))
                return await Stop(ZipChildIngestionStop.OutsideChildIngestionBoundary, ordinal);
            if (entry.Retained is null || entry.EvidenceJson is null)
                throw new InvalidDataException("Child ingestion requires retained Clean scan evidence.");
            var scan = JsonSerializer.Deserialize<MalwareScanEvidence>(entry.EvidenceJson)
                ?? throw new InvalidDataException("Missing ZIP scan evidence.");
            MalwareScanValidation.Evidence(scan);
            if (!scan.IsClean || scan.Request.Content != new MalwareScanContent(entry.Retained.ContentId,
                    entry.Retained.Revision, entry.Retained.Sha256, entry.Retained.Length))
                throw new InvalidDataException("Child ingestion requires exact retained Clean evidence.");
            if (entry.Plan.ChildOperationId is null || entry.Plan.ProvisionalArtifactId is null)
                throw new InvalidDataException("Child ingestion identity is missing.");
            var gate = new ZipChildIngestionEffectGate(attempt, parents, () => parent, ordinal);
            if (entry.State == ZipEntryState.Ingested)
            {
                if (attempt.Purpose != ZipOwnershipPurpose.Recovery)
                    throw new UnauthorizedAccessException("Recorded child ingestion requires fresh Recovery authority.");
                var existing = await attempt.OpenChildAsync(parent, entry, gate, ct);
                await At("Resuming");
                var resumed = await existing.Coordinator.ResumeAsync(ct);
                if (resumed.AuditDelivery != IngestionAuditDelivery.Completed)
                    throw new InvalidDataException("Recorded ZIP ingestion cannot reconcile audit authority.");
                var proof = await ProofAsync(existing, entry, ct);
                if (entry.Ingestion is null || entry.Ingestion with { IngestionRevision = proof.IngestionRevision } != proof ||
                    proof.IngestionRevision < entry.Ingestion.IngestionRevision)
                    throw new InvalidDataException("ZIP ingestion recovery evidence changed.");
                return await Stop(ZipChildIngestionStop.Ingested, ordinal);
            }
            ZipChildIngestionRuntime? runtime = null;
            bool exists;
            var reuseCompleted = false;
            if (attempt.Purpose == ZipOwnershipPurpose.Recovery &&
                await ingestions.HasIngestionReservationAsync(parent.Fence, ordinal, ct))
            {
                runtime = await attempt.OpenChildAsync(parent, entry, gate, ct);
                await using var previous = await runtime.Persistence.AcquireAsync(new(entry.Plan.ChildOperationId), ct);
                if (previous.IsDamaged || previous.HasReview)
                    throw new InvalidDataException("Original ZIP child ingestion requires review.");
                reuseCompleted = previous.Intent?.State is ArtifactIngestionState.Completed or ArtifactIngestionState.Cleaned;
            }
            if (!reuseCompleted)
            {
                var reservation = new ZipWorkReservation(Guid.NewGuid().ToString("N"), ZipWorkKind.Ingestion, ordinal);
                await using (var scope = await gate.OpenAsync(ZipChildIngestionEffect.Reservation, ct))
                {
                    parent = await parents.ReserveAsync(parent.Fence, reservation, scope, ct);
                    await scope.FinishAsync(ct);
                }
                await At("Reserved");
                runtime = await attempt.OpenChildAsync(parent, entry, gate, ct);
            }
            await using (var session = await runtime!.Persistence.AcquireAsync(new(entry.Plan.ChildOperationId), ct))
            {
                if (session.IsDamaged || session.HasReview)
                    throw new InvalidDataException("Original ZIP child ingestion requires review.");
                exists = session.Intent is not null;
            }
            if (exists && attempt.Purpose != ZipOwnershipPurpose.Recovery)
                throw new UnauthorizedAccessException("Original ingestion replay requires fresh Recovery authority.");
            ArtifactIngestionOutcome outcome;
            if (exists)
            {
                await At("Resuming");
                outcome = await runtime.Coordinator.ResumeAsync(ct);
            }
            else
            {
                await using var scope = await gate.OpenAsync(ZipChildIngestionEffect.RetainedRead, ct);
                await using var lease = await retention.OpenForIngestionAsync(parent, ordinal, scope, ct);
                if (lease.ArtifactId.Value != entry.Retained.ContentId ||
                    lease.Revision.Value != entry.Retained.Revision || lease.ReturnedLength != entry.Retained.Length)
                    throw new InvalidDataException("ZIP ingestion plaintext lease changed.");
                var id = new ArtifactId(entry.Plan.ProvisionalArtifactId);
                var draft = new IngestionMetadataDraft(new Artifact
                {
                    Id = id, Name = entry.Plan.DisplayName, ArtifactType = "file",
                    Fingerprint = new() { Algorithm = "SHA-256", Value = entry.Retained.Sha256 }
                }, new Provenance
                {
                    ArtifactId = id,
                    Source = "zip-parent:" + parent.Binding.ParentArtifactId + "/entry:" + entry.Plan.CentralOrdinal,
                    RecordedBy = "zip-extraction"
                });
                // Retained plaintext remains owned until the coordinator has returned
                // or failed. Disposal always clears it before proof progression.
                await At("Ingesting");
                outcome = await runtime.Coordinator.IngestAsync(draft, lease.Content, ct);
                await scope.FinishAsync(ct);
            }
            await At("CoordinatorReturned");
            if (outcome.OperationId.Value != entry.Plan.ChildOperationId ||
                outcome.Disposition is not (ArtifactIngestionDisposition.ProvisionalArtifactAdopted or
                    ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact))
                throw new InvalidDataException("Original ZIP ingestion has no successful durable outcome.");
            if (outcome.AuditDelivery != IngestionAuditDelivery.Completed)
                throw new InvalidDataException("ZIP ingestion audit delivery is incomplete.");
            var verified = await ProofAsync(runtime, entry, ct);
            await At("OutcomeVerified");
            await using (var scope = await gate.OpenAsync(ZipChildIngestionEffect.Proof, ct))
            {
                parent = await ingestions.RecordIngestionAsync(parent.Fence, ordinal, verified, scope, ct);
                await scope.FinishAsync(ct);
            }
            await At("Recorded");
            return await Stop(ZipChildIngestionStop.Ingested, ordinal);

            async Task<ZipChildIngestionResult> Stop(ZipChildIngestionStop reason, int? file)
            {
                await attempt.RevalidateAsync(ct); ct.ThrowIfCancellationRequested(); attempt.Validate(parent);
                return new(reason, parent, file);
            }
        }
        finally { ZipChildWorkGate.Gate.Release(); }
    }
}
