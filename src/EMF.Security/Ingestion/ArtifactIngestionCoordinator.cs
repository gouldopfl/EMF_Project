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
        var operation = await _context.GetIngestionOperationAsync(cancellationToken);
        Actor(operation.ActorId); ArtifactContentIdentity.Validate(operation.OperationId.Value); ArtifactContentIdentity.Validate(operation.AuthorizedOperationId.Value);
        if (draft.Artifact.Id != draft.Provenance.ArtifactId || draft.Artifact.Fingerprint is null
            || await _fingerprints.ComputeAsync(content, cancellationToken) != draft.Artifact.Fingerprint)
            throw new InvalidDataException("Invalid ingestion metadata/content binding.");
        // Canonical audit resource bounds are validated before protected creation.
        if (System.Text.Encoding.UTF8.GetByteCount(draft.Artifact.Id.Value) > 128)
            throw new ArgumentException("Artifact identity exceeds ingestion audit schema bounds.");
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
                await session.PrepareAsync(intent, new(operation.AuthorizedOperationId, operation.ActorId), draft, cancellationToken);
            }
            else
            {
                var saved = await session.ReadDraftAsync(cancellationToken);
                if (session.OperationBinding != new IngestionOperationBinding(operation.AuthorizedOperationId, operation.ActorId)
                    || saved?.Provenance.Source != draft.Provenance.Source || saved.Artifact.Fingerprint != draft.Artifact.Fingerprint)
                    throw new ArtifactContentIdempotencyException();
                var authority = await session.ResolveAuthorityAsync(session.Intent.ArtifactId, cancellationToken);
                if (authority is null || await Authorize(operation.ActorId, authority, false, cancellationToken) != AuthorizationDecision.Allow)
                    throw new UnauthorizedAccessException("Artifact ingestion replay was denied.");
            }
            await session.CommitAsync(cancellationToken);
        }
        await CreateAsync(operation.OperationId, content, operation.ActorId, false, cancellationToken);
        var committed = await AdoptAsync(operation.OperationId, operation.ActorId, cancellationToken);
        // Independent bounded delivery work cannot erase a known metadata commit.
        using var deliveryBudget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { return await FinishAuthorizedAsync(operation.OperationId, operation.ActorId, false, deliveryBudget.Token); }
        catch (Exception error) when (committed is not null)
        {
            return committed with { AuditDelivery = error is InvalidDataException ? IngestionAuditDelivery.RequiresReview : IngestionAuditDelivery.Pending,
                State = error is InvalidDataException ? ArtifactIngestionState.RequiresReview : committed.State,
                SafeFailureCategory = error is InvalidDataException ? "LifecycleDamage" : "DeliveryRecoveryPending" };
        }
    }
    private async Task CreateAsync(ArtifactContentOperationId operationId, ReadOnlyMemory<byte>? plaintext, string actor, bool recovery, CancellationToken ct)
    {
        // Serialize candidate selection under the same cross-process operation fence.
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
            var receipt = await _physical.GetMutationOutcomeAsync(operationId, ct);
            if (receipt is not null)
            {
                if (!CreateReceiptMatches(intent, receipt)) await RecordReviewAsync(session, "CreationReceiptConflict", actor, ct, isRecovery: recovery);
                else await session.RecordCreatedAsync(intent, receipt, ct);
                await session.CommitAsync(ct); return;
            }
            var candidate = await _staging.ReadAsync(operationId, ct);
            if (intent.CandidateHash is not null)
            {
                if (candidate is null || Digest(candidate) != intent.CandidateHash)
                { await RecordReviewAsync(session, "CandidateIntegrityFailure", actor, ct, isRecovery: recovery); await session.CommitAsync(ct); return; }
            }
            else
            {
                if (plaintext is null)
                {
                    // No candidate was admitted for mutation. Only this fenced Prepared case
                    // can end without a physical deletion; a current object is contradictory.
                    if (await _physical.ReadVersionedAsync(intent.ArtifactId, ct) is not null)
                        await RecordReviewAsync(session, "UnexpectedPhysicalContent", actor, ct, isRecovery: recovery);
                    else await session.RecordCleanedAsync(intent, null, actor, ct);
                    await session.CommitAsync(ct); return;
                }
                if (candidate is null)
                {
                    var envelope = await _encryption.EncryptWithContextAsync(plaintext.Value, ArtifactEnvelopeContext.Create(intent.ArtifactId), ct);
                    EncryptedEnvelopeFormat.Validate(envelope);
                    if (envelope.FormatVersion != EncryptedEnvelopeFormat.ContextBoundVersion) throw new CryptographicException("Ingestion requires context-bound encryption.");
                    candidate = JsonSerializer.SerializeToUtf8Bytes(envelope);
                    await _staging.StageAsync(operationId, candidate, ct);
                }
                else
                {
                    // An interrupted staging acknowledgement must match the logical request.
                    var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(candidate) ?? throw new InvalidDataException("Invalid staged candidate.");
                    var recovered = await _encryption.DecryptWithContextAsync(envelope, ArtifactEnvelopeContext.Create(intent.ArtifactId), ct);
                    try { if (!CryptographicOperations.FixedTimeEquals(recovered, plaintext.Value.Span)) throw new ArtifactContentIdempotencyException(); }
                    finally { CryptographicOperations.ZeroMemory(recovered); }
                }
                await session.SetCandidateAsync(intent, Digest(candidate), ct);
            }
            // Candidate digest and Prepared binding are durable BEFORE physical creation.
            await session.CommitAsync(ct);
        }
        await using (var session = await _persistence.AcquireAsync(operationId, ct))
        {
            var intent = session.Intent;
            if (intent?.State != ArtifactIngestionState.Prepared || session.IsDamaged) { await session.CommitAsync(ct); return; }
            if (await session.HasAdoptionEvidenceAsync(intent.ArtifactId, ct))
            { await RecordReviewAsync(session, "ContradictoryAdoption", actor, ct, isRecovery: recovery); await session.CommitAsync(ct); return; }
            var authority = await session.ResolveAuthorityAsync(intent.ArtifactId, ct);
            if (!ProvisionalMatches(intent, authority) || await Authorize(actor, authority!, recovery, ct) != AuthorizationDecision.Allow)
            { await RecordReviewAsync(session, "ProvisionalAuthorityFailure", actor, ct, isRecovery: recovery); await session.CommitAsync(ct); return; }
            var candidate = await _staging.ReadAsync(operationId, ct);
            if (candidate is null || Digest(candidate) != intent.CandidateHash)
            { await RecordReviewAsync(session, "CandidateIntegrityFailure", actor, ct, isRecovery: recovery); await session.CommitAsync(ct); return; }
            var receipt = await _physical.GetMutationOutcomeAsync(operationId, ct);
            if (receipt is null)
            {
                try
                {
                    receipt = (await _physical.CreateIfAbsentAsync(intent.ArtifactId, candidate,
                        new(operationId, intent.OwnershipToken, intent.CreateEventId, true), ct)).Receipt;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    try { receipt = await _physical.GetMutationOutcomeAsync(operationId, ct); }
                    catch { /* no speculative replay after an unavailable outcome */ }
                    if (receipt is null)
                    { await RecordReviewAsync(session, "CreationOutcomeUnknown", actor, ct, isRecovery: recovery); await session.CommitAsync(ct); return; }
                }
            }
            await session.EnsureReceiptAuditAsync(receipt, ct);
            if (!CreateReceiptMatches(intent, receipt)) await RecordReviewAsync(session, "CreationReceiptConflict", actor, ct, isRecovery: recovery);
            else await session.RecordCreatedAsync(intent, receipt, ct);
            await session.CommitAsync(ct);
        }
    }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool CreateReceiptMatches(ArtifactIngestionIntent intent, ArtifactContentMutationReceipt receipt) =>
        intent.CandidateHash is not null && receipt.OperationId == intent.OperationId && receipt.ArtifactId == intent.ArtifactId
        && receipt.OwnershipToken == intent.OwnershipToken && receipt.Kind == ArtifactContentMutationKind.Create
        && receipt.Outcome == ArtifactContentMutationOutcome.Created && receipt.PriorRevision is null && receipt.CurrentRevision is not null
        && receipt.AuditEventId == intent.CreateEventId && receipt.AuditObligationVersion == 1;
    private async Task<ArtifactIngestionOutcome?> AdoptAsync(ArtifactContentOperationId operationId, string actor, CancellationToken ct)
    {
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
        var receipt = await _physical.GetMutationOutcomeAsync(operationId, ct);
        var physical = await _physical.ReadVersionedAsync(intent.ArtifactId, ct);
        if (receipt is null || receipt != intent.CreateReceipt || !CreateReceiptMatches(intent, receipt)
            || physical is null || physical.Revision != receipt.CurrentRevision || Digest(physical.Content) != intent.CandidateHash)
        { await RecordReviewAsync(session, "CreationEvidenceFailure", actor, ct, isRecovery: false); await session.CommitAsync(ct); return null; }
        var draft = await session.ReadDraftAsync(ct);
        if (draft is null || !await _classification.CanAdoptAsync(authority!, draft.Artifact, ct))
        { await RecordReviewAsync(session, "AdoptionClassificationMismatch", actor, ct, isRecovery: false); await session.CommitAsync(ct); return null; }
        var canonical = await session.FindCanonicalAsync(ct);
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
            if (agrees) agrees = await CanonicalIntegrityAsync(canonical, ct);
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
    private async Task<bool> CanonicalIntegrityAsync(IngestionMetadataDraft canonical, CancellationToken ct)
    {
        // Canonical repair is a separately authorized lifecycle, never an ingestion overwrite.
        var snapshot = await _physical.ReadVersionedAsync(canonical.Artifact.Id, ct);
        if (snapshot is null || canonical.Artifact.Fingerprint is null) return false;
        var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(snapshot.Content) ?? throw new InvalidDataException("Invalid canonical envelope.");
        var plaintext = await _encryption.DecryptWithContextAsync(envelope, ArtifactEnvelopeContext.Create(canonical.Artifact.Id), ct);
        try { return await _fingerprints.ComputeAsync(plaintext, ct) == canonical.Artifact.Fingerprint; }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    public async Task<ArtifactIngestionOutcome> RecoverOperationAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default)
    {
        var actor = await _context.GetRecoveryActorAsync(cancellationToken); Actor(actor);
        try
        {
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
                        var receipt = await _physical.GetMutationOutcomeAsync(operationId, cancellationToken);
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
            if (session.OperationBinding is not null && session.OperationBinding != new IngestionOperationBinding(operation.AuthorizedOperationId, operation.ActorId))
                throw new ArtifactContentIdempotencyException();
        }
        return await RecoverOperationAsync(operation.OperationId, cancellationToken);
    }
    private async Task CleanupAsync(ArtifactContentOperationId operationId, string actor, CancellationToken ct)
    {
        // Claim is durably committed before physical deletion.
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
            var receipt = await _physical.GetMutationOutcomeAsync(operationId, ct);
            if (receipt is null || receipt != intent.CreateReceipt || !CreateReceiptMatches(intent, receipt))
            { await RecordReviewAsync(session, "CreationEvidenceFailure", actor, ct); await session.CommitAsync(ct); return; }
            await session.ClaimCleanupAsync(intent, authority!, actor, ct); await session.CommitAsync(ct);
        }
        await using (var session = await _persistence.AcquireAsync(operationId, ct))
        {
            var intent = session.Intent;
            // Adoption truth has precedence even if a bypassing writer changed
            // mutable lifecycle state while this cleanup worker was paused.
            if (session.ProvisionalArtifactId is { } id && await session.HasAdoptionEvidenceAsync(id, ct))
            { await RecordReviewAsync(session, "ContradictoryAdoption", actor, ct); await session.CommitAsync(ct); return; }
            if (intent?.State != ArtifactIngestionState.CleanupClaimed || session.IsDamaged) { await session.CommitAsync(ct); return; }
            var authority = await session.ResolveAuthorityAsync(intent.ArtifactId, ct);
            if (!ProvisionalMatches(intent, authority) || await Authorize(actor, authority!, true, ct) != AuthorizationDecision.Allow)
            { await RecordReviewAsync(session, "CleanupAuthorityFailure", actor, ct); await session.CommitAsync(ct); return; }
            // Revalidate the original creation receipt again after reacquiring the
            // deletion fence. Mutable receipt damage between claim and execution
            // cannot substitute a later generation for the exact created revision.
            var creation = await _physical.GetMutationOutcomeAsync(operationId, ct);
            if (creation is null || creation != intent.CreateReceipt || !CreateReceiptMatches(intent, creation))
            { await RecordReviewAsync(session, "CreationEvidenceFailure", actor, ct); await session.CommitAsync(ct); return; }
            // Recheck the claim under the lease through physical promotion; classification/adoption cannot race it.
            await session.ClaimCleanupAsync(intent, authority!, actor, ct);
            var receipt = await _physical.GetMutationOutcomeAsync(intent.CleanupOperationId, ct);
            if (receipt is null)
            {
                try
                {
                    receipt = (await _physical.DeleteIfRevisionMatchesAsync(intent.ArtifactId, intent.CreateReceipt!.CurrentRevision!.Value,
                        new(intent.CleanupOperationId, intent.OwnershipToken, intent.CleanupEventId, true), ct)).Receipt;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    try { receipt = await _physical.GetMutationOutcomeAsync(intent.CleanupOperationId, ct); } catch { }
                    if (receipt is null)
                    { await RecordReviewAsync(session, "CleanupOutcomeUnknown", actor, ct); await session.CommitAsync(ct); return; }
                }
            }
            await session.EnsureReceiptAuditAsync(receipt, ct);
            if (receipt.OperationId != intent.CleanupOperationId || receipt.ArtifactId != intent.ArtifactId || receipt.Kind != ArtifactContentMutationKind.Delete
                || receipt.Outcome != ArtifactContentMutationOutcome.Deleted || receipt.PriorRevision != intent.CreateReceipt?.CurrentRevision
                || receipt.OwnershipToken != intent.OwnershipToken || receipt.AuditEventId != intent.CleanupEventId || receipt.AuditObligationVersion != 1 || receipt.CurrentRevision is null)
                await RecordReviewAsync(session, "CleanupReceiptConflict", actor, ct);
            else await session.RecordCleanedAsync(intent, receipt, actor, ct);
            await session.CommitAsync(ct);
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
            await using (var session = await _persistence.AcquireAsync(operationId.Value, cancellationToken))
            {
                // No repair, outbox reconstruction or lifecycle transition precedes
                // the current resource/classification recovery authorization decision.
                authorizedRecovery = await AuthorizeReconciliationAsync(session, actor, true, cancellationToken);
                needsReview = session.Intent is null || session.IsDamaged;
                if (authorizedRecovery)
                {
                    var conflict = false;
                    try { await VerifyReceiptAcknowledgementAsync(receipt, cancellationToken); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) when (error is SecurityAuditIdentityConflictException or InvalidDataException or ArgumentException)
                    { conflict = true; }
                    catch { /* Chain unavailable: never infer acknowledgement from a local flag. */ }
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
        await using (var repair = await _persistence.AcquireAsync(operationId, cancellationToken))
        {
            var authorized = await AuthorizeReconciliationAsync(repair, actor, recovery, cancellationToken);
            reviewOnly = !authorized || repair.IsDamaged || repair.Intent is null;
            if (authorized && !repair.IsDamaged && repair.Intent is { } repairIntent)
            {
                try
                {
                    if (repairIntent.CreateReceipt is not null)
                    {
                        var receipt = await _physical.GetMutationOutcomeAsync(operationId, cancellationToken);
                        if (receipt is null || receipt != repairIntent.CreateReceipt || !CreateReceiptMatches(repairIntent, receipt))
                            throw new InvalidDataException("Creation receipt evidence is unavailable.");
                        await repair.EnsureReceiptAuditAsync(receipt, cancellationToken);
                    }
                    if (repairIntent.CleanupReceipt is not null)
                    {
                        var receipt = await _physical.GetMutationOutcomeAsync(repairIntent.CleanupOperationId, cancellationToken);
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
