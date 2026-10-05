using System.Security.Cryptography;
using System.Text.Json;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Models;
using EMF.Security.Storage;

namespace EMF.Security.Ingestion;

public sealed class ArtifactIngestionCoordinator : IArtifactIngestionCoordinator
{
    public const int MaximumPlaintextBytes = 100 * 1024 * 1024;
    public const int MaximumCandidateBytes = 150 * 1024 * 1024;
    public static readonly TimeSpan ExecutionBudget = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan RecoveryBudget = TimeSpan.FromSeconds(30);
    private readonly IArtifactIngestionPersistence _persistence;
    private readonly IVersionedArtifactContentStore _physical;
    private readonly IArtifactContentStagingStore _staging;
    private readonly IEnvelopeEncryptionService _encryption;
    private readonly IArtifactIngestionSecurityContext _context;
    private readonly IArtifactIngestionClassificationPolicy _classification;
    private readonly IAuthorizationPolicy _authorization;
    private readonly IAcknowledgedSecurityAuditSink _audit;
    private readonly IContentFingerprintService _fingerprints;
    public ArtifactIngestionCoordinator(IArtifactIngestionPersistence persistence, IVersionedArtifactContentStore physical,
        IArtifactContentStagingStore staging, IEnvelopeEncryptionService encryption, IArtifactIngestionSecurityContext context,
        IArtifactIngestionClassificationPolicy classification, IAuthorizationPolicy authorization,
        IAcknowledgedSecurityAuditSink audit, IContentFingerprintService fingerprints)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _physical = physical ?? throw new ArgumentNullException(nameof(physical));
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _classification = classification ?? throw new ArgumentNullException(nameof(classification));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _fingerprints = fingerprints ?? throw new ArgumentNullException(nameof(fingerprints));
    }
    private static void Actor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || new System.Text.UTF8Encoding(false, true).GetByteCount(actor) > 256 || actor.Any(char.IsControl))
            throw new UnauthorizedAccessException("Invalid authenticated ingestion actor.");
    }
    private Task<AuthorizationDecision> Authorize(string actor, IngestionClassificationAuthority authority, bool recovery, CancellationToken ct)
        => _authorization.EvaluateAsync(new() { SubjectId = actor, ResourceType = SecurityResourceTypes.Artifact,
            ResourceId = authority.ArtifactId.Value, ProtectionClassificationId = new(authority.ClassificationId.Value),
            PermissionId = recovery ? SecurityPermissions.ArtifactIngestionRecover : SecurityPermissions.ArtifactIngest }, ct);
    private async Task RecordReviewAsync(IArtifactIngestionSession session, string condition, string actor, CancellationToken ct, bool isRecovery = true)
    {
        Actor(actor);
        // Review is separately authorized without asserting missing classification
        // authority. Unknown resource identity is left unknown for policy to deny.
        if (!await _context.AuthorizeNonDestructiveReviewAsync(actor, session.OperationId, session.ProvisionalArtifactId, ct))
            throw new UnauthorizedAccessException("Non-destructive ingestion review was denied; durable work is retained.");
        await session.RecordReviewAsync(condition, actor, ct, isRecovery);
    }
    private async Task<bool> AuthorizeReconciliationAsync(IArtifactIngestionSession session, string actor, bool recovery, CancellationToken ct)
    {
        // Authentication is not recovery permission. Check current authority before
        // repairing delivery work or changing lifecycle/acknowledgement bookkeeping.
        var authority = session.ProvisionalArtifactId is { } id ? await session.ResolveAuthorityAsync(id, ct) : null;
        if (authority is not null && (recovery || session.OperationBinding?.OriginalActorId == actor)
            && await Authorize(actor, authority, recovery, ct) == AuthorizationDecision.Allow) return true;
        await RecordReviewAsync(session, "RecoveryAuthorizationDenied", actor, ct, isRecovery: recovery);
        return false; // Separately authorized review never grants reconciliation/cleanup authority.
    }
    private static bool ProvisionalMatches(ArtifactIngestionIntent intent, IngestionClassificationAuthority? authority) =>
        authority is not null && !authority.IsAdopted && authority.ArtifactId == intent.ArtifactId
        && authority.OwnershipToken == intent.OwnershipToken && authority.AuthorizedOperationId == intent.AuthorizedOperationId
        && authority.ClassificationId == intent.ClassificationId && authority.Revision == intent.ClassificationRevision;

    public async Task<ArtifactIngestionOutcome> IngestAsync(IngestionMetadataDraft draft, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (content.Length > MaximumPlaintextBytes) throw new InvalidDataException("Ingestion plaintext exceeds the admitted bound.");
        using var executionBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        executionBudget.CancelAfter(ExecutionBudget);
        cancellationToken = executionBudget.Token;
        var operation = await _context.GetIngestionOperationAsync(cancellationToken);
        Actor(operation.ActorId); ArtifactContentIdentity.Validate(operation.OperationId.Value); ArtifactContentIdentity.Validate(operation.AuthorizedOperationId.Value);
        if (draft.Artifact.Id != draft.Provenance.ArtifactId || draft.Artifact.Fingerprint is null
            || await _fingerprints.ComputeAsync(content, cancellationToken) != draft.Artifact.Fingerprint)
            throw new InvalidDataException("Invalid ingestion metadata/content binding.");
        // Canonical audit resource bounds are validated before protected creation.
        if (System.Text.Encoding.UTF8.GetByteCount(draft.Artifact.Id.Value) > 128)
            throw new ArgumentException("Artifact identity exceeds ingestion audit schema bounds.");
        _ = PreparedStore; // Admit capability before persisting any new intent.
        using var execution = await _persistence.AcquireExecutionAsync(operation.OperationId, cancellationToken);
        await using (var session = await _persistence.AcquireAsync(operation.OperationId, cancellationToken))
        {
            if (session.IsDamaged)
            {
                await RecordReviewAsync(session, "LifecycleDamage", operation.ActorId, cancellationToken, isRecovery: false); await session.CommitAsync(cancellationToken);
                throw new ArtifactIngestionReviewException(operation.OperationId);
            }
            if (session.Intent is null)
            {
                var classification = await _classification.ResolveProvisionalAsync(operation, cancellationToken)
                    ?? throw new UnauthorizedAccessException("Provisional classification is unresolved.");
                var intent = new ArtifactIngestionIntent(operation.OperationId, draft.Artifact.Id, new(Guid.NewGuid().ToString("N")),
                    new(classification.Value), IngestionClassificationRevision.New(), operation.AuthorizedOperationId,
                    ArtifactContentOperationId.New(), new("ingestion.create." + Guid.NewGuid().ToString("N")), new("ingestion.adopt." + Guid.NewGuid().ToString("N")),
                    new("ingestion.cleanup." + Guid.NewGuid().ToString("N")), DateTimeOffset.UtcNow);
                var authority = new IngestionClassificationAuthority(intent.ArtifactId, intent.ClassificationId, intent.ClassificationRevision,
                    false, intent.OwnershipToken, intent.AuthorizedOperationId);
                if (await Authorize(operation.ActorId, authority, false, cancellationToken) != AuthorizationDecision.Allow)
                    throw new UnauthorizedAccessException("Artifact ingestion was denied.");
                if (!await _classification.CanAdoptAsync(authority, draft.Artifact, cancellationToken))
                    throw new UnauthorizedAccessException("Proposed artifact classification does not reconcile with authority.");
                // Validate the approved audit schema before admitting this operation.
                _ = SecurityAuditCanonicalEvent.Encode(Event(new(intent.CreateEventId, intent.OperationId, intent.ArtifactId,
                    intent.ClassificationId, intent.ClassificationRevision, operation.ActorId, operation.ActorId, false, IngestionAuditAction.Created, intent.PreparedUtc)));
                await session.PrepareAsync(intent, new(operation.AuthorizedOperationId, operation.ActorId, operation.ParentOperationId), draft, cancellationToken);
            }
            else
            {
                var saved = await session.ReadDraftAsync(cancellationToken);
                if (session.OperationBinding != new IngestionOperationBinding(operation.AuthorizedOperationId, operation.ActorId, operation.ParentOperationId)
                    || saved is null || JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(draft))
                    throw new ArtifactContentIdempotencyException();
                var authority = await session.ResolveAuthorityAsync(session.Intent.ArtifactId, cancellationToken);
                if (authority is null || await Authorize(operation.ActorId, authority, false, cancellationToken) != AuthorizationDecision.Allow)
                    throw new UnauthorizedAccessException("Artifact ingestion replay was denied.");
            }
            await session.CommitAsync(cancellationToken);
        }
        return await PromoteAndFinishAsync(operation.OperationId, content, operation.ActorId, execution, cancellationToken);
    }
    public async Task<ArtifactIngestionOutcome> ResumeAsync(CancellationToken cancellationToken = default)
    {
        using var executionBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        executionBudget.CancelAfter(ExecutionBudget);
        cancellationToken = executionBudget.Token;
        var operation = await _context.GetIngestionOperationAsync(cancellationToken);
        Actor(operation.ActorId); _ = PreparedStore;
        using var execution = await _persistence.AcquireExecutionAsync(operation.OperationId, cancellationToken);
        await using (var session = await _persistence.AcquireAsync(operation.OperationId, cancellationToken))
        {
            if (session.IsDamaged || session.Intent is null)
            {
                await RecordReviewAsync(session, "LifecycleDamage", operation.ActorId, cancellationToken, isRecovery: false);
                await session.CommitAsync(cancellationToken); throw new ArtifactIngestionReviewException(operation.OperationId);
            }
            if (session.OperationBinding != new IngestionOperationBinding(operation.AuthorizedOperationId, operation.ActorId, operation.ParentOperationId))
                throw new ArtifactContentIdempotencyException();
            var authority = await session.ResolveAuthorityAsync(session.Intent.ArtifactId, cancellationToken);
            if (authority is null || await Authorize(operation.ActorId, authority, false, cancellationToken) != AuthorizationDecision.Allow)
                throw new UnauthorizedAccessException("Artifact ingestion resume was denied.");
            await session.CommitAsync(cancellationToken);
        }
        // Only the original durable draft/candidate may supply restart inputs.
        return await PromoteAndFinishAsync(operation.OperationId, null, operation.ActorId, execution, cancellationToken);
    }
    private async Task<ArtifactIngestionOutcome> PromoteAndFinishAsync(ArtifactContentOperationId operationId,
        ReadOnlyMemory<byte>? plaintext, string actor, IDisposable execution, CancellationToken cancellationToken)
    {
        try { await CreateAsync(operationId, plaintext, actor, false, cancellationToken); }
        catch (Exception error) when (error is not OperationCanceledException && error is not ArtifactContentIdempotencyException)
        {
            // The failed promotion session is disposed before querying a lost response.
            if (await _physical.GetMutationOutcomeAsync(operationId, cancellationToken) is null) throw;
            await CreateAsync(operationId, plaintext, actor, false, cancellationToken);
        }
        var committed = await AdoptAsync(operationId, actor, cancellationToken);
        execution.Dispose();
        // Independent bounded delivery work cannot erase a known metadata commit.
        using var deliveryBudget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { return await FinishAuthorizedAsync(operationId, actor, false, deliveryBudget.Token); }
        catch (Exception error) when (committed is not null)
        {
            return committed with { AuditDelivery = error is InvalidDataException ? IngestionAuditDelivery.RequiresReview : IngestionAuditDelivery.Pending,
                State = error is InvalidDataException ? ArtifactIngestionState.RequiresReview : committed.State,
                SafeFailureCategory = error is InvalidDataException ? "LifecycleDamage" : "DeliveryRecoveryPending" };
        }
    }
    private async Task CreateAsync(ArtifactContentOperationId operationId, ReadOnlyMemory<byte>? plaintext, string actor, bool recovery, CancellationToken ct)
    {
        ArtifactIngestionIntent expected;
        IngestionMetadataDraft draft;
        bool mayEncrypt = false;
        // Caller holds operation execution coordination, never a broad authority fence.
        await using (var session = await _persistence.AcquireAsync(operationId, ct))
        {
            var intent = session.Intent;
            if (session.IsDamaged || intent is null)
            { await RecordReviewAsync(session, "LifecycleDamage", actor, ct, isRecovery: recovery); await session.CommitAsync(ct); return; }
            if (intent.State != ArtifactIngestionState.Prepared) { await session.CommitAsync(ct); return; }
            if (await session.HasAdoptionEvidenceAsync(intent.ArtifactId, ct))
            { await RecordReviewAsync(session, "ContradictoryAdoption", actor, ct, isRecovery: recovery); await session.CommitAsync(ct); return; }
            var authority = await session.ResolveAuthorityAsync(intent.ArtifactId, ct);
            if (!ProvisionalMatches(intent, authority) || await Authorize(actor, authority!, recovery, ct) != AuthorizationDecision.Allow)
            { await RecordReviewAsync(session, "ProvisionalAuthorityFailure", actor, ct, isRecovery: recovery); await session.CommitAsync(ct); return; }
            draft = await session.ReadDraftAsync(ct) ?? throw new InvalidDataException("Missing ingestion draft.");
            expected = intent;
            if (intent.CandidateHash is null && !session.CandidatePreparationStarted && plaintext is not null)
            {
                await session.BeginCandidatePreparationAsync(intent, ct);
                mayEncrypt = true;
            }
            await session.CommitAsync(ct);
        }
        // Receipt and protected candidate reads, cryptography, and hashing are detached.
        var receipt = await _physical.GetMutationOutcomeAsync(operationId, ct);
        var candidate = await _staging.ReadAsync(operationId, ct);
        if (candidate is null && expected.CandidateHash is null && receipt is null && mayEncrypt)
        {
            var envelope = await _encryption.EncryptWithContextAsync(plaintext!.Value, ArtifactEnvelopeContext.Create(expected.ArtifactId), ct);
            EncryptedEnvelopeFormat.Validate(envelope);
            if (envelope.FormatVersion != EncryptedEnvelopeFormat.ContextBoundVersion) throw new CryptographicException("Ingestion requires context-bound encryption.");
            candidate = JsonSerializer.SerializeToUtf8Bytes(envelope);
            if (candidate.Length > MaximumCandidateBytes) throw new InvalidDataException("Ingestion candidate exceeds the admitted bound.");
            await _staging.StageAsync(operationId, candidate, ct);
        }
        if (candidate is null && expected.CandidateHash is null && receipt is null)
        {
            // Execution coordination excludes a live producer. A lost randomized candidate
            // is never regenerated under this logical identity, even when plaintext is supplied.
            var current = await PreparedStore.ReadCurrentRevisionAsync(expected.ArtifactId, ct);
            await using var empty = await _persistence.AcquireAsync(operationId, ct);
            if (!await RevalidateAsync(empty, expected, actor, recovery, ct, draft)) { await empty.CommitAsync(ct); return; }
            if (current is not null) await RecordReviewAsync(empty, "UnexpectedPhysicalContent", actor, ct, isRecovery: recovery);
            else await empty.RecordCleanedAsync(expected, null, actor, ct);
            await empty.CommitAsync(ct); return;
        }
        if (candidate?.Length > MaximumCandidateBytes) throw new InvalidDataException("Ingestion candidate exceeds the admitted bound.");
        var hash = candidate is null ? null : Digest(candidate);
        if (hash is null || expected.CandidateHash is not null && hash != expected.CandidateHash)
        {
            await using var damaged = await _persistence.AcquireAsync(operationId, ct);
            await RecordReviewAsync(damaged, "CandidateIntegrityFailure", actor, ct, isRecovery: recovery); await damaged.CommitAsync(ct); return;
        }
        if (expected.CandidateHash is null)
        {
            // Lost staging acknowledgement: verify the retained request, never use decryption
            // as ownership evidence. Ownership comes solely from durable authority/intent.
            var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(candidate!) ?? throw new InvalidDataException("Invalid staged candidate.");
            EncryptedEnvelopeFormat.Validate(envelope);
            if (envelope.FormatVersion != EncryptedEnvelopeFormat.ContextBoundVersion) throw new CryptographicException("Ingestion requires context-bound encryption.");
            var recovered = await _encryption.DecryptWithContextAsync(envelope, ArtifactEnvelopeContext.Create(expected.ArtifactId), ct);
            try
            {
                if (plaintext is not null && !CryptographicOperations.FixedTimeEquals(recovered, plaintext.Value.Span)) throw new ArtifactContentIdempotencyException();
                if (await _fingerprints.ComputeAsync(recovered, ct) != draft.Artifact.Fingerprint) throw new ArtifactContentIdempotencyException();
            }
            finally { CryptographicOperations.ZeroMemory(recovered); }
            await using var binding = await _persistence.AcquireAsync(operationId, ct);
            if (!await RevalidateAsync(binding, expected, actor, recovery, ct, draft)) { await binding.CommitAsync(ct); return; }
            await binding.SetCandidateAsync(expected, hash, ct);
            expected = binding.Intent!;
            await binding.CommitAsync(ct);
        }
        // Provider owns generation/GC coordination before metadata coordination is acquired.
        // Even lost-response retrieval occurs detached; receipt identity is checked on re-entry.
        await using var prepared = receipt is null
            ? await PreparedStore.PreparePhysicalCreateAsync(expected.ArtifactId, candidate!,
                new(operationId, expected.OwnershipToken, expected.CreateEventId, true), ct) : null;
        await using (var promotion = await _persistence.AcquireAsync(operationId, ct))
        {
            if (!await RevalidateAsync(promotion, expected, actor, recovery, ct, draft)) { await promotion.CommitAsync(ct); return; }
            if (receipt is null)
            {
                // Only short physical catalog promotion runs under the authority fence.
                // Uncertain outcomes escape this session for detached receipt reconciliation.
                receipt = (await prepared!.ExecuteAsync(ct)).Receipt;
            }
            await promotion.EnsureReceiptAuditAsync(receipt, ct);
            if (!CreateReceiptMatches(expected, receipt)) await RecordReviewAsync(promotion, "CreationReceiptConflict", actor, ct, isRecovery: recovery);
            else await promotion.RecordCreatedAsync(expected, receipt, ct);
            await promotion.CommitAsync(ct);
        }
    }
    private IPreparedArtifactContentStore PreparedStore => _physical as IPreparedArtifactContentStore
        ?? throw new NotSupportedException("Ingestion requires detached physical preparation and bounded revision validation.");
    private async Task<bool> RevalidateAsync(IArtifactIngestionSession session, ArtifactIngestionIntent expected, string actor, bool recovery, CancellationToken ct, IngestionMetadataDraft? expectedDraft = null)
    {
        if (session.IsDamaged || session.Intent != expected || session.HasReview
            || await session.HasAdoptionEvidenceAsync(expected.ArtifactId, ct)
            || expectedDraft is not null && JsonSerializer.Serialize(await session.ReadDraftAsync(ct)) != JsonSerializer.Serialize(expectedDraft))
        { await RecordReviewAsync(session, "LifecycleDamage", actor, ct, isRecovery: recovery); return false; }
        var authority = await session.ResolveAuthorityAsync(expected.ArtifactId, ct);
        if (!ProvisionalMatches(expected, authority) || await Authorize(actor, authority!, recovery, ct) != AuthorizationDecision.Allow)
        { await RecordReviewAsync(session, "ProvisionalAuthorityFailure", actor, ct, isRecovery: recovery); return false; }
        return true;
    }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool CreateReceiptMatches(ArtifactIngestionIntent intent, ArtifactContentMutationReceipt receipt) =>
        intent.CandidateHash is not null && receipt.OperationId == intent.OperationId && receipt.ArtifactId == intent.ArtifactId
        && receipt.OwnershipToken == intent.OwnershipToken && receipt.Kind == ArtifactContentMutationKind.Create
        && receipt.Outcome == ArtifactContentMutationOutcome.Created && receipt.PriorRevision is null && receipt.CurrentRevision is not null
        && receipt.AuditEventId == intent.CreateEventId && receipt.AuditObligationVersion == 1;
    private async Task<ArtifactIngestionOutcome?> AdoptAsync(ArtifactContentOperationId operationId, string actor, CancellationToken ct)
    {
        ArtifactIngestionIntent? capturedIntent;
        IngestionMetadataDraft? capturedDraft;
        IngestionMetadataDraft? capturedCanonical;
        IngestionClassificationAuthority? capturedCanonicalAuthority;
        await using (var capture = await _persistence.AcquireAsync(operationId, ct))
        {
            capturedIntent = capture.Intent;
            if (capturedIntent?.State == ArtifactIngestionState.ContentCreated && !capture.IsDamaged)
            {
                if (!await RevalidateAsync(capture, capturedIntent, actor, false, ct)) { await capture.CommitAsync(ct); return null; }
                capturedDraft = await capture.ReadDraftAsync(ct);
                capturedCanonical = await capture.FindCanonicalAsync(ct);
                capturedCanonicalAuthority = capturedCanonical is null ? null : await capture.ResolveAuthorityAsync(capturedCanonical.Artifact.Id, ct);
                if (capturedCanonical is not null && (capturedCanonicalAuthority is not { IsAdopted: true }
                    || await Authorize(actor, capturedCanonicalAuthority, false, ct) != AuthorizationDecision.Allow))
                {
                    // No canonical bytes may be copied before their own authorization.
                    capturedCanonicalAuthority = null;
                }
            }
            else { capturedDraft = null; capturedCanonical = null; capturedCanonicalAuthority = null; }
            await capture.CommitAsync(ct);
        }
        var receipt = capturedIntent?.State == ArtifactIngestionState.ContentCreated ? await _physical.GetMutationOutcomeAsync(operationId, ct) : null;
        var physical = capturedIntent?.State == ArtifactIngestionState.ContentCreated ? await _physical.ReadVersionedAsync(capturedIntent.ArtifactId, ct) : null;
        var physicalHash = physical is null ? null : Digest(physical.Content);
        var canonicalPhysical = capturedCanonicalAuthority is null ? null : await _physical.ReadVersionedAsync(capturedCanonical!.Artifact.Id, ct);
        var canonicalIntegrity = canonicalPhysical is not null && await CanonicalIntegrityAsync(capturedCanonical!, canonicalPhysical, ct);
        await using var physicalProbe = physical is null ? null : await PreparedStore.PrepareRevisionValidationAsync(capturedIntent!.ArtifactId, ct);
        await using var canonicalProbe = canonicalPhysical is null ? null : await PreparedStore.PrepareRevisionValidationAsync(capturedCanonical!.Artifact.Id, ct);
        await using var session = await _persistence.AcquireAsync(operationId, ct);
        var intent = session.Intent;
        if (intent?.State != ArtifactIngestionState.ContentCreated || session.IsDamaged)
        {
            ArtifactIngestionOutcome? known = null;
            if (!session.IsDamaged && intent?.Disposition is ArtifactIngestionDisposition.ProvisionalArtifactAdopted or ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact)
                known = new(operationId, intent.State, intent.Disposition, await session.ReadResultAsync(ct),
                    await session.HasAdoptionEvidenceAsync(intent.ArtifactId, ct), IngestionAuditDelivery.Pending);
            await session.CommitAsync(ct); return known;
        }
        if (await session.HasAdoptionEvidenceAsync(intent.ArtifactId, ct))
        { await RecordReviewAsync(session, "ContradictoryAdoption", actor, ct, isRecovery: false); await session.CommitAsync(ct); return null; }
        var authority = await session.ResolveAuthorityAsync(intent.ArtifactId, ct);
        if (!ProvisionalMatches(intent, authority) || await Authorize(actor, authority!, false, ct) != AuthorizationDecision.Allow)
        { await RecordReviewAsync(session, "ProvisionalAuthorityFailure", actor, ct, isRecovery: false); await session.CommitAsync(ct); return null; }
        if (intent != capturedIntent || receipt is null || receipt != intent.CreateReceipt || !CreateReceiptMatches(intent, receipt)
            || physical is null || physical.Revision != receipt.CurrentRevision || physicalHash != intent.CandidateHash
            || await physicalProbe!.ReadCurrentRevisionAsync(ct) != physical.Revision)
        { await RecordReviewAsync(session, "CreationEvidenceFailure", actor, ct, isRecovery: false); await session.CommitAsync(ct); return null; }
        var draft = await session.ReadDraftAsync(ct);
        if (draft is null || JsonSerializer.Serialize(draft) != JsonSerializer.Serialize(capturedDraft)
            || !await _classification.CanAdoptAsync(authority!, draft.Artifact, ct))
        { await RecordReviewAsync(session, "AdoptionClassificationMismatch", actor, ct, isRecovery: false); await session.CommitAsync(ct); return null; }
        var canonical = await session.FindCanonicalAsync(ct);
        if (JsonSerializer.Serialize(canonical) != JsonSerializer.Serialize(capturedCanonical))
        { await RecordReviewAsync(session, "CanonicalReconciliationFailure", actor, ct, isRecovery: false); await session.CommitAsync(ct); return null; }
        IngestionClassificationAuthority? canonicalAuthority = null;
        string? cleanupActor = null;
        if (canonical is not null)
        {
            canonicalAuthority = await session.ResolveAuthorityAsync(canonical.Artifact.Id, ct);
            var recoveryActor = await _context.GetRecoveryActorAsync(ct); Actor(recoveryActor);
            if (await Authorize(recoveryActor, authority!, true, ct) == AuthorizationDecision.Allow) cleanupActor = recoveryActor;
            var agrees = canonicalAuthority is { IsAdopted: true } && canonicalAuthority.ClassificationId == authority!.ClassificationId
                && await _classification.CanonicalClassificationAgreesAsync(authority!, canonicalAuthority, ct)
                && await Authorize(actor, canonicalAuthority, false, ct) == AuthorizationDecision.Allow;
            if (agrees) agrees = canonicalAuthority == capturedCanonicalAuthority && canonicalIntegrity
                && await canonicalProbe!.ReadCurrentRevisionAsync(ct) == canonicalPhysical!.Revision;
            if (!agrees)
            {
                await session.RecordDeduplicationConflictAsync(intent, authority!, canonical.Artifact.Id, cleanupActor, ct);
                await session.CommitAsync(ct); return null;
            }
            if (cleanupActor is null)
            { await RecordReviewAsync(session, "CleanupAuthorizationUnavailable", actor, ct, isRecovery: false); await session.CommitAsync(ct); return null; }
        }
        await session.AdoptAsync(intent, authority!, canonicalAuthority, cleanupActor, ct);
        var adoptedIntent = session.Intent!;
        var result = await session.ReadResultAsync(ct);
        var committed = new ArtifactIngestionOutcome(operationId, adoptedIntent.State, adoptedIntent.Disposition, result,
            adoptedIntent.Disposition == ArtifactIngestionDisposition.ProvisionalArtifactAdopted, IngestionAuditDelivery.Pending);
        await session.CommitAsync(ct);
        return committed;
    }
    private async Task<bool> CanonicalIntegrityAsync(IngestionMetadataDraft canonical, ArtifactContentSnapshot snapshot, CancellationToken ct)
    {
        // Canonical repair is a separately authorized lifecycle, never an ingestion overwrite.
        if (canonical.Artifact.Fingerprint is null) return false;
        var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(snapshot.Content) ?? throw new InvalidDataException("Invalid canonical envelope.");
        var plaintext = await _encryption.DecryptWithContextAsync(envelope, ArtifactEnvelopeContext.Create(canonical.Artifact.Id), ct);
        try { return await _fingerprints.ComputeAsync(plaintext, ct) == canonical.Artifact.Fingerprint; }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    public async Task<ArtifactIngestionOutcome> RecoverOperationAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default)
    {
        using var recoveryBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        recoveryBudget.CancelAfter(RecoveryBudget);
        cancellationToken = recoveryBudget.Token;
        var actor = await _context.GetRecoveryActorAsync(cancellationToken); Actor(actor);
        using var execution = await _persistence.AcquireExecutionAsync(operationId, cancellationToken);
        try
        {
            var recoveryReceipt = await _physical.GetMutationOutcomeAsync(operationId, cancellationToken);
            await using (var session = await _persistence.AcquireAsync(operationId, cancellationToken))
            {
                var id = session.ProvisionalArtifactId;
                var adopted = id is not null && await session.HasAdoptionEvidenceAsync(id.Value, cancellationToken);
                var intent = session.Intent;
                if (session.IsDamaged || intent is null)
                    await RecordReviewAsync(session, "LifecycleDamage", actor, cancellationToken);
                else if (adopted)
                {
                    var authority = await session.ResolveAuthorityAsync(intent.ArtifactId, cancellationToken);
                    if (intent.State is not (ArtifactIngestionState.MetadataCommitted or ArtifactIngestionState.Completed)
                        || authority is not { IsAdopted: true } || await session.ReadResultAsync(cancellationToken) is null)
                        await RecordReviewAsync(session, "ContradictoryAdoption", actor, cancellationToken);
                    else if (await Authorize(actor, authority, true, cancellationToken) != AuthorizationDecision.Allow)
                        await RecordReviewAsync(session, "RecoveryAuthorizationDenied", actor, cancellationToken);
                    else
                    {
                        var receipt = recoveryReceipt;
                        if (receipt is null || receipt != intent.CreateReceipt || !CreateReceiptMatches(intent, receipt))
                            await RecordReviewAsync(session, "CreationEvidenceFailure", actor, cancellationToken);
                        else await session.RecordRecoveryCompletionAsync(actor, cancellationToken);
                    }
                }
                await session.CommitAsync(cancellationToken);
                if (adopted || session.IsDamaged || intent is null)
                    return await FinishAsync(operationId, cancellationToken);
            }
            await CreateAsync(operationId, null, actor, true, cancellationToken);
            await CleanupAsync(operationId, actor, cancellationToken);
            return await FinishAsync(operationId, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Persist a sanitized review category. If persistence is unavailable, propagate
            // a typed pending-work failure; never manufacture successful recovery.
            try
            {
                await using var session = await _persistence.AcquireAsync(operationId, cancellationToken);
                await RecordReviewAsync(session, "RecoveryEvidenceFailure", actor, cancellationToken); await session.CommitAsync(cancellationToken);
                return await FinishAsync(operationId, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch { throw new ArtifactIngestionReviewException(operationId); }
        }
    }
    public async Task<ArtifactIngestionOutcome?> RecoverInterruptedAsync(CancellationToken cancellationToken = default)
    {
        var operation = await _context.GetIngestionOperationAsync(cancellationToken);
        await using (var session = await _persistence.AcquireAsync(operation.OperationId, cancellationToken))
        {
            if (session.ProvisionalArtifactId is null && !session.IsDamaged) return null;
            if (session.OperationBinding is not null && session.OperationBinding != new IngestionOperationBinding(operation.AuthorizedOperationId, operation.ActorId, operation.ParentOperationId))
                throw new ArtifactContentIdempotencyException();
        }
        return await RecoverOperationAsync(operation.OperationId, cancellationToken);
    }
    private async Task CleanupAsync(ArtifactContentOperationId operationId, string actor, CancellationToken ct)
    {
        // Claim is durably committed before physical deletion.
        var creationEvidence = await _physical.GetMutationOutcomeAsync(operationId, ct);
        ArtifactIngestionIntent claimed;
        await using (var session = await _persistence.AcquireAsync(operationId, ct))
        {
            var intent = session.Intent;
            if (intent?.State is not (ArtifactIngestionState.ContentCreated or ArtifactIngestionState.CleanupClaimed) || session.IsDamaged)
            { await session.CommitAsync(ct); return; }
            if (await session.HasAdoptionEvidenceAsync(intent.ArtifactId, ct))
            { await RecordReviewAsync(session, "ContradictoryAdoption", actor, ct); await session.CommitAsync(ct); return; }
            var authority = await session.ResolveAuthorityAsync(intent.ArtifactId, ct);
            if (!ProvisionalMatches(intent, authority) || await Authorize(actor, authority!, true, ct) != AuthorizationDecision.Allow)
            { await RecordReviewAsync(session, "CleanupAuthorityFailure", actor, ct); await session.CommitAsync(ct); return; }
            var receipt = creationEvidence;
            if (receipt is null || receipt != intent.CreateReceipt || !CreateReceiptMatches(intent, receipt))
            { await RecordReviewAsync(session, "CreationEvidenceFailure", actor, ct); await session.CommitAsync(ct); return; }
            await session.ClaimCleanupAsync(intent, authority!, actor, ct);
            claimed = session.Intent!;
            await session.CommitAsync(ct);
        }
        creationEvidence = await _physical.GetMutationOutcomeAsync(operationId, ct);
        var deletionReceipt = await _physical.GetMutationOutcomeAsync(claimed.CleanupOperationId, ct);
        await using var preparedDelete = deletionReceipt is null ? await PreparedStore.PreparePhysicalDeleteAsync(claimed.ArtifactId,
            claimed.CreateReceipt!.CurrentRevision!.Value, new(claimed.CleanupOperationId, claimed.OwnershipToken, claimed.CleanupEventId, true), ct) : null;
        Exception? deletionFailure = null;
        await using (var session = await _persistence.AcquireAsync(operationId, ct))
        {
            var intent = session.Intent;
            // Adoption truth has precedence even if a bypassing writer changed
            // mutable lifecycle state while this cleanup worker was paused.
            if (session.ProvisionalArtifactId is { } id && await session.HasAdoptionEvidenceAsync(id, ct))
            { await RecordReviewAsync(session, "ContradictoryAdoption", actor, ct); await session.CommitAsync(ct); return; }
            if (session.IsDamaged || intent?.State == ArtifactIngestionState.CleanupClaimed && intent != claimed)
            { await RecordReviewAsync(session, "LifecycleDamage", actor, ct); await session.CommitAsync(ct); return; }
            if (intent?.State != ArtifactIngestionState.CleanupClaimed) { await session.CommitAsync(ct); return; }
            var authority = await session.ResolveAuthorityAsync(intent.ArtifactId, ct);
            if (!ProvisionalMatches(intent, authority) || await Authorize(actor, authority!, true, ct) != AuthorizationDecision.Allow)
            { await RecordReviewAsync(session, "CleanupAuthorityFailure", actor, ct); await session.CommitAsync(ct); return; }
            // Revalidate the original creation receipt again after reacquiring the
            // deletion fence. Mutable receipt damage between claim and execution
            // cannot substitute a later generation for the exact created revision.
            var creation = creationEvidence;
            if (creation is null || creation != intent.CreateReceipt || !CreateReceiptMatches(intent, creation))
            { await RecordReviewAsync(session, "CreationEvidenceFailure", actor, ct); await session.CommitAsync(ct); return; }
            // Recheck the claim under the lease through physical promotion; classification/adoption cannot race it.
            await session.ClaimCleanupAsync(intent, authority!, actor, ct);
            var receipt = deletionReceipt;
            if (receipt is null)
            {
                try { receipt = (await preparedDelete!.ExecuteAsync(ct)).Receipt; }
                catch (Exception error) when (error is not OperationCanceledException) { deletionFailure = error; }
            }
            if (receipt is not null)
            {
                await session.EnsureReceiptAuditAsync(receipt, ct);
                if (receipt.OperationId != intent.CleanupOperationId || receipt.ArtifactId != intent.ArtifactId || receipt.Kind != ArtifactContentMutationKind.Delete
                    || receipt.Outcome != ArtifactContentMutationOutcome.Deleted || receipt.PriorRevision != intent.CreateReceipt?.CurrentRevision
                    || receipt.OwnershipToken != intent.OwnershipToken || receipt.AuditEventId != intent.CleanupEventId || receipt.AuditObligationVersion != 1 || receipt.CurrentRevision is null)
                    await RecordReviewAsync(session, "CleanupReceiptConflict", actor, ct);
                else await session.RecordCleanedAsync(intent, receipt, actor, ct);
                await session.CommitAsync(ct);
            }
            // Unknown outcomes roll back/dispose before detached receipt discovery.
        }
        if (deletionFailure is not null)
        {
            var known = await _physical.GetMutationOutcomeAsync(claimed.CleanupOperationId, ct);
            if (known is not null) await CleanupAsync(operationId, actor, ct); // Receipt recognition; never another deletion.
            else
            {
                await using var review = await _persistence.AcquireAsync(operationId, ct);
                await RecordReviewAsync(review, "CleanupOutcomeUnknown", actor, ct); await review.CommitAsync(ct);
            }
        }
    }
    public async Task<IReadOnlyList<ArtifactIngestionOutcome>> RecoverBatchAsync(long afterCursor, int limit, CancellationToken cancellationToken = default)
    {
        var work = await _persistence.ReadRecoveryWorkAsync(afterCursor, limit, cancellationToken);
        var result = new List<ArtifactIngestionOutcome>();
        foreach (var item in work) result.Add(await RecoverOperationAsync(item.OperationId, cancellationToken));
        return result;
    }
    // Receipt-backed reconciliation remains independent of intent existence. This entry point
    // exposes the durable physical cursor; the host retains it and schedules bounded scans.
    public async Task<ArtifactContentReceiptCursor?> ReconcileReceiptsAsync(ArtifactContentReceiptCursor? afterCursor, int limit, CancellationToken cancellationToken = default)
    {
        var actor = await _context.GetRecoveryActorAsync(cancellationToken); Actor(actor);
        var obligations = await _physical.ReadAuditObligationsAsync(afterCursor, limit, cancellationToken);
        foreach (var obligation in obligations)
        {
            var receipt = obligation.Receipt;
            var operationId = await _persistence.FindReceiptOperationAsync(receipt, cancellationToken);
            if (operationId is null)
            {
                if (receipt.AuditEventId?.Value.StartsWith("ingestion.", StringComparison.Ordinal) == true)
                {
                    // Discovery is read-only. Even orphan-review bookkeeping requires
                    // separately authorized non-destructive review before its first write.
                    if (!await _context.AuthorizeNonDestructiveReviewAsync(actor, receipt.OperationId, receipt.ArtifactId, cancellationToken))
                        throw new UnauthorizedAccessException("Receipt review was denied; durable receipt work is retained.");
                    try { await VerifyReceiptAcknowledgementAsync(receipt, cancellationToken); }
                    catch (OperationCanceledException) { throw; }
                    catch { /* Unknown/conflicting acknowledgement remains orphan review work. */ }
                    await _persistence.RecordOrphanReceiptAsync(receipt, cancellationToken);
                }
                afterCursor = obligation.Cursor;
                continue;
            }
            bool needsReview;
            bool authorizedRecovery;
            await using (var admission = await _persistence.AcquireAsync(operationId.Value, cancellationToken))
            {
                authorizedRecovery = await AuthorizeReconciliationAsync(admission, actor, true, cancellationToken);
                await admission.CommitAsync(cancellationToken);
            }
            var conflict = false;
            if (authorizedRecovery)
            {
                try { await VerifyReceiptAcknowledgementAsync(receipt, cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) when (error is SecurityAuditIdentityConflictException or InvalidDataException or ArgumentException) { conflict = true; }
                catch { /* Chain unavailable: never infer acknowledgement from a local flag. */ }
            }
            await using (var session = await _persistence.AcquireAsync(operationId.Value, cancellationToken))
            {
                // No repair, outbox reconstruction or lifecycle transition precedes
                // the current resource/classification recovery authorization decision.
                authorizedRecovery = authorizedRecovery && await AuthorizeReconciliationAsync(session, actor, true, cancellationToken);
                needsReview = session.Intent is null || session.IsDamaged;
                if (authorizedRecovery)
                {
                    if (conflict || needsReview)
                    {
                        await RecordReviewAsync(session, conflict ? "ReceiptAuditConflict" : "ReceiptWithoutValidIntent", actor, cancellationToken);
                        needsReview = true;
                    }
                    else
                    {
                        try { await session.EnsureReceiptAuditAsync(receipt, cancellationToken); }
                        catch (InvalidDataException)
                        { await RecordReviewAsync(session, "ReceiptAuditConflict", actor, cancellationToken); needsReview = true; }
                    }
                }
                await session.CommitAsync(cancellationToken);
            }
            if (needsReview && authorizedRecovery)
                await _persistence.RecordOrphanReceiptAsync(receipt, cancellationToken);
            // Review delivery is separate: it never acknowledges the receipt's
            // original event or confers permission for full reconciliation.
            await FinishAuthorizedAsync(operationId.Value, actor, true, cancellationToken);
            afterCursor = obligation.Cursor;
        }
        return afterCursor;
    }
    private async Task VerifyReceiptAcknowledgementAsync(ArtifactContentMutationReceipt receipt, CancellationToken ct)
    {
        if (receipt.AuditEventId is null || receipt.AuditObligationVersion != 1)
            throw new InvalidDataException("Receipt audit obligation is unsupported.");
        // Always query the original receipt event, even when only a review event
        // can safely be delivered because mutable lifecycle state was lost.
        var verified = await _audit.FindVerifiedAsync(new(receipt.AuditEventId.Value.Value), ct);
        if (verified is null) return;
        if (!ReceiptMatchesEvent(receipt, verified.Record)
            || verified.Acknowledgement.EventId.Value != receipt.AuditEventId.Value.Value
            || verified.Acknowledgement.RecordId <= 0 || string.IsNullOrWhiteSpace(verified.Acknowledgement.RecordHash))
            throw new SecurityAuditIdentityConflictException();
        var canonical = SecurityAuditCanonicalEvent.Encode(verified.Record);
        var original = await _persistence.ReadReceiptAuditObligationAsync(receipt, ct);
        if (original is not null && !canonical.AsSpan().SequenceEqual(SecurityAuditCanonicalEvent.Encode(Event(original))))
            throw new SecurityAuditIdentityConflictException();
    }
    public async Task<ArtifactIngestionOutcome> FinishAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default)
    {
        var actor = await _context.GetRecoveryActorAsync(cancellationToken); Actor(actor);
        return await FinishAuthorizedAsync(operationId, actor, true, cancellationToken);
    }
    private async Task<ArtifactIngestionOutcome> FinishAuthorizedAsync(ArtifactContentOperationId operationId, string actor, bool recovery, CancellationToken cancellationToken)
    {
        var delivery = IngestionAuditDelivery.Completed;
        var validAuditEvidence = true;
        var reviewOnly = false;
        ArtifactIngestionIntent? receiptIntent;
        await using (var lookup = await _persistence.AcquireAsync(operationId, cancellationToken))
        { receiptIntent = lookup.Intent; await lookup.CommitAsync(cancellationToken); }
        ArtifactContentMutationReceipt? creationEvidence = null;
        ArtifactContentMutationReceipt? cleanupEvidence = null;
        var receiptEvidenceFailure = false;
        try
        {
            if (receiptIntent?.CreateReceipt is not null) creationEvidence = await _physical.GetMutationOutcomeAsync(operationId, cancellationToken);
            if (receiptIntent?.CleanupReceipt is not null) cleanupEvidence = await _physical.GetMutationOutcomeAsync(receiptIntent.CleanupOperationId, cancellationToken);
        }
        catch (InvalidDataException) { receiptEvidenceFailure = true; }
        await using (var repair = await _persistence.AcquireAsync(operationId, cancellationToken))
        {
            var authorized = await AuthorizeReconciliationAsync(repair, actor, recovery, cancellationToken);
            reviewOnly = !authorized || repair.IsDamaged || repair.Intent is null;
            if (authorized && !repair.IsDamaged && repair.Intent is { } repairIntent)
            {
                try
                {
                    if (receiptEvidenceFailure) throw new InvalidDataException("Receipt evidence is damaged.");
                    if (repairIntent.CreateReceipt is not null)
                    {
                        var receipt = creationEvidence;
                        if (receipt is null || receipt != repairIntent.CreateReceipt || !CreateReceiptMatches(repairIntent, receipt))
                            throw new InvalidDataException("Creation receipt evidence is unavailable.");
                        await repair.EnsureReceiptAuditAsync(receipt, cancellationToken);
                    }
                    if (repairIntent.CleanupReceipt is not null)
                    {
                        var receipt = cleanupEvidence;
                        if (receipt is null || receipt != repairIntent.CleanupReceipt) throw new InvalidDataException("Cleanup receipt evidence is unavailable.");
                        await repair.EnsureReceiptAuditAsync(receipt, cancellationToken);
                    }
                    await repair.EnsureAdoptionAuditAsync(cancellationToken);
                }
                catch (InvalidDataException)
                {
                    validAuditEvidence = false;
                    delivery = IngestionAuditDelivery.RequiresReview;
                    await RecordReviewAsync(repair, "AuditEvidenceFailure", actor, cancellationToken, isRecovery: recovery);
                }
            }
            await repair.CommitAsync(cancellationToken);
        }
        try
        {
            IReadOnlyList<IngestionAuditObligation> obligations;
            if (!validAuditEvidence || reviewOnly)
            {
                var review = await _persistence.ReadReviewAuditObligationAsync(operationId, cancellationToken);
                obligations = review is null ? Array.Empty<IngestionAuditObligation>() : new[] { review };
            }
            else obligations = await _persistence.ReadAuditObligationsAsync(operationId, cancellationToken);
            foreach (var obligation in obligations)
            {
                await _persistence.MarkAuditPendingAsync(obligation, cancellationToken);
                var record = Event(obligation);
                var verified = await _audit.FindVerifiedAsync(new(obligation.EventId.Value), cancellationToken);
                SecurityAuditAcknowledgement acknowledgement;
                if (verified is not null)
                {
                    if (!SecurityAuditCanonicalEvent.Encode(verified.Record).AsSpan().SequenceEqual(SecurityAuditCanonicalEvent.Encode(record)))
                        throw new SecurityAuditIdentityConflictException();
                    acknowledgement = verified.Acknowledgement;
                }
                else
                {
                    acknowledgement = await _audit.AppendAsync(record, cancellationToken);
                    verified = await _audit.FindVerifiedAsync(new(obligation.EventId.Value), cancellationToken);
                    if (verified is null || verified.Acknowledgement != acknowledgement ||
                        !SecurityAuditCanonicalEvent.Encode(verified.Record).AsSpan().SequenceEqual(SecurityAuditCanonicalEvent.Encode(record)))
                        throw new SecurityAuditIdentityConflictException();
                }
                if (acknowledgement.EventId.Value != obligation.EventId.Value || acknowledgement.RecordId <= 0 || string.IsNullOrWhiteSpace(acknowledgement.RecordHash))
                    throw new SecurityAuditIdentityConflictException();
                await _persistence.AcknowledgeAuditAsync(obligation, cancellationToken);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is SecurityAuditIdentityConflictException or InvalidDataException)
        {
            delivery = IngestionAuditDelivery.RequiresReview;
            await using var review = await _persistence.AcquireAsync(operationId, cancellationToken);
            await RecordReviewAsync(review, error is InvalidDataException ? "AuditJournalDamage" : "AuditIdentityConflict", actor, cancellationToken, isRecovery: recovery); await review.CommitAsync(cancellationToken);
        }
        catch { delivery = IngestionAuditDelivery.Pending; }
        await using var session = await _persistence.AcquireAsync(operationId, cancellationToken);
        var intent = session.Intent;
        var adopted = session.ProvisionalArtifactId is { } id && await session.HasAdoptionEvidenceAsync(id, cancellationToken);
        var reopen = delivery == IngestionAuditDelivery.Pending && intent?.State == ArtifactIngestionState.Completed && adopted;
        var complete = delivery == IngestionAuditDelivery.Completed && intent?.State == ArtifactIngestionState.MetadataCommitted;
        if ((reopen || complete) && !session.IsDamaged && !session.HasReview)
        {
            // Delivery ran outside the metadata lease. Reauthorize current authority
            // under the new fence before an authoritative lifecycle transition.
            if (await AuthorizeReconciliationAsync(session, actor, recovery, cancellationToken))
            {
                if (reopen) await session.ReopenAuditDeliveryAsync(intent!, cancellationToken);
                else await session.CompleteAsync(intent!, cancellationToken);
            }
            else delivery = IngestionAuditDelivery.RequiresReview;
            intent = session.Intent;
        }
        var result = intent?.Disposition is ArtifactIngestionDisposition.ProvisionalArtifactAdopted or ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact
            ? await session.ReadResultAsync(cancellationToken) : null;
        var outcome = new ArtifactIngestionOutcome(operationId, session.IsDamaged ? ArtifactIngestionState.RequiresReview : intent?.State ?? ArtifactIngestionState.RequiresReview,
            intent?.Disposition ?? ArtifactIngestionDisposition.None, result, adopted,
            session.HasReview || session.IsDamaged ? IngestionAuditDelivery.RequiresReview : delivery, intent?.SafeFailureCategory);
        await session.CommitAsync(cancellationToken); return outcome;
    }
    private static SecurityAuditRecord Event(IngestionAuditObligation obligation) => new()
    {
        AuditEventId = new(obligation.EventId.Value), OperationId = new((obligation.MutationOperationId ?? obligation.OperationId).Value),
        OriginalActorId = obligation.OriginalActorId, RecoveryActorId = obligation.IsRecovery ? obligation.ExecutingActorId : null,
        SubjectId = obligation.ExecutingActorId, Operation = SecurityPermissions.ArtifactIngest.Value,
        ResourceType = SecurityResourceTypes.Artifact, ResourceId = obligation.ArtifactId.Value,
        PolicyDecision = AuthorizationDecision.Allow,
        Outcome = obligation.Action is IngestionAuditAction.RequiresReview or IngestionAuditAction.CreationRejected or IngestionAuditAction.CleanupRejected
            ? SecurityAuditOutcome.Failed : SecurityAuditOutcome.Succeeded,
        OccurredUtc = obligation.OccurredUtc,
        Facts = Facts(obligation)
    };
    private static IReadOnlyDictionary<string, string> Facts(IngestionAuditObligation obligation)
    {
        var facts = new Dictionary<string, string> { ["classificationId"] = obligation.ClassificationId.Value,
            ["classificationRevision"] = obligation.ClassificationRevision.Value, ["disposition"] = obligation.Action.ToString(),
            ["ingestionSchemaVersion"] = obligation.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (obligation.SafeFailureCategory is { } condition) facts.Add("recoveryCondition", condition);
        return facts;
    }
    private static bool ReceiptMatchesEvent(ArtifactContentMutationReceipt receipt, SecurityAuditRecord record)
    {
        var action = receipt.Kind == ArtifactContentMutationKind.Create
            ? receipt.Outcome == ArtifactContentMutationOutcome.Created ? IngestionAuditAction.Created : IngestionAuditAction.CreationRejected
            : receipt.Outcome == ArtifactContentMutationOutcome.Deleted ? IngestionAuditAction.Deleted : IngestionAuditAction.CleanupRejected;
        return record.AuditEventId?.Value == receipt.AuditEventId?.Value && record.OperationId?.Value == receipt.OperationId.Value
            && record.ResourceId == receipt.ArtifactId.Value && record.ResourceType == SecurityResourceTypes.Artifact
            && record.Operation == SecurityPermissions.ArtifactIngest.Value && record.OccurredUtc == receipt.OccurredUtc
            && record.Outcome == (action is IngestionAuditAction.Created or IngestionAuditAction.Deleted ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Failed)
            && record.Facts.TryGetValue("disposition", out var disposition) && disposition == action.ToString();
    }
}
