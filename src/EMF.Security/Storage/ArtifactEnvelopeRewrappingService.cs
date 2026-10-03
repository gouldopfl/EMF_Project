using System.Security.Cryptography;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Models;
using EMF.Security.Storage.Models;
namespace EMF.Security.Storage;

public sealed class ArtifactEnvelopeRewrappingService : IArtifactEnvelopeRewrappingService
{
    private readonly IVersionedArtifactContentStore _contentStore;
    private readonly IAuthenticatedEnvelopeKeyRewrappingService _rewrappingService;
    private readonly IAuthorizationPolicy _authorizationPolicy;
    private readonly IAcknowledgedSecurityAuditSink _auditSink;
    private readonly IArtifactMutationAuthority _authority;
    private readonly IArtifactRewrapJournal _journal;
    private readonly IArtifactContentStagingStore _staging;
    public ArtifactEnvelopeRewrappingService(IVersionedArtifactContentStore contentStore,
        IAuthenticatedEnvelopeKeyRewrappingService rewrappingService, IAuthorizationPolicy authorizationPolicy,
        IAcknowledgedSecurityAuditSink auditSink, IArtifactMutationAuthority authority,
        IArtifactRewrapJournal journal, IArtifactContentStagingStore staging)
    {
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _rewrappingService = rewrappingService ?? throw new ArgumentNullException(nameof(rewrappingService));
        _authorizationPolicy = authorizationPolicy ?? throw new ArgumentNullException(nameof(authorizationPolicy));
        _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
    }
    public async Task<ArtifactEnvelopeRewrappingResult> RewrapAsync(ArtifactEnvelopeRewrappingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        SecurityAuditIdentity.Validate(request.OperationId.Value);
        AuthorizationDecision? decision = null;
        string? previous = null, current = null;
        ArtifactRewrapIntent? intent = null;
        var promotionStarted = false;
        ArtifactContentMutationReceipt? observedReceipt = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var authority = await _authority.AcquireAsync(request.ArtifactId, cancellationToken);
            if (authority.ArtifactId != request.ArtifactId || !authority.IsAdopted ||
                authority.ClassificationId != request.ProtectionClassificationId)
                throw new UnauthorizedAccessException("Authoritative classification or adoption does not permit ordinary rewrap.");
            decision = await AuthorizeAsync(request.SubjectId, authority, cancellationToken);
            if (decision != AuthorizationDecision.Allow) throw new UnauthorizedAccessException("Artifact envelope rewrapping was denied.");
            var receipt = await _contentStore.GetMutationOutcomeAsync(new(request.OperationId.Value), cancellationToken);
            observedReceipt = receipt;
            try { intent = await _journal.ReadAsync(request.OperationId, cancellationToken); }
            catch (InvalidDataException) when (receipt is not null) { return await MissingIntentAsync(request, receipt); }
            if (intent is not null && (intent.ArtifactId != request.ArtifactId || intent.OriginalActorId != request.SubjectId ||
                intent.ClassificationId != request.ProtectionClassificationId)) throw new ArtifactContentIdempotencyException();
            if (receipt is not null)
            {
                if (intent is null) return await MissingIntentAsync(request, receipt);
                promotionStarted = true;
                return await CompleteAsync(intent, receipt);
            }
            if (intent is { Receipt: not null }) return await ReviewAsync(intent, "MissingReceipt");
            if (intent?.Event is not null) return await DeliverAsync(intent);
            if (intent?.State == ArtifactRewrapState.RequiresReview) return Result(intent, null, ArtifactAuditDeliveryState.RequiresReview);
            if (intent is not null && intent.ClassificationRevision != authority.ClassificationRevision)
                return await ReviewAsync(intent, "ClassificationChanged");
            if (intent?.State == ArtifactRewrapState.Prepared)
            {
                var staged = await _staging.ReadAsync(new(intent.OperationId.Value), cancellationToken);
                if (staged is null || Convert.ToHexString(SHA256.HashData(staged)) != intent.CandidateHash)
                    return await ReviewAsync(intent, "StagingIntegrityFailure");
                cancellationToken.ThrowIfCancellationRequested(); promotionStarted = true;
                return await PromoteAsync(intent, staged, cancellationToken);
            }
            var snapshot = await _contentStore.ReadVersionedAsync(request.ArtifactId, cancellationToken);
            if (snapshot is null)
            {
                if (intent is not null) return await NonMutationAsync(intent, ArtifactEnvelopeRewrappingOutcome.NotFound);
                await WritePrePromotionAuditAsync(request, decision, SecurityAuditOutcome.Failed, null, null);
                return new()
                {
                    ArtifactId = request.ArtifactId,
                    OperationId = request.OperationId,
                    Outcome = ArtifactEnvelopeRewrappingOutcome.NotFound,
                    CompletedUtc = DateTimeOffset.UtcNow,
                    AuditDelivery = ArtifactAuditDeliveryState.Completed
                };
            }
            var envelope = ValidateEnvelope(JsonSerializer.Deserialize<EncryptedEnvelope>(snapshot.Content));
            if (envelope.FormatVersion != EncryptedEnvelopeFormat.ContextBoundVersion)
                throw new CryptographicException("Artifact rewrap requires identity-bound content.");
            previous = envelope.KeyEncryptionKeyId;
            if (intent is null)
            {
                intent = new(request.OperationId, SecurityAuditEventId.New(), request.ArtifactId, request.SubjectId,
                    authority.ClassificationId, authority.ClassificationRevision, snapshot.Revision, DateTimeOffset.UtcNow, PreviousKeyId: previous);
                // Validate the complete bounded event schema before any physical mutation.
                _ = SecurityAuditCanonicalEvent.Encode(CreateEvent(intent, ArtifactEnvelopeRewrappingOutcome.Updated, intent.AuthorizedUtc));
                await _journal.CreateAsync(intent, cancellationToken); // durable authorized intent precedes candidate preparation
            }
            if (intent.ExpectedRevision != snapshot.Revision && intent.State == ArtifactRewrapState.Authorized)
                return await NonMutationAsync(intent, ArtifactEnvelopeRewrappingOutcome.VersionConflict);
            var candidate = await _staging.ReadAsync(new(intent.OperationId.Value), cancellationToken);
            if (intent.State == ArtifactRewrapState.Prepared)
            {
                if (candidate is null || Convert.ToHexString(SHA256.HashData(candidate)) != intent.CandidateHash)
                    return await ReviewAsync(intent, "StagingIntegrityFailure");
            }
            else
            {
                if (candidate is null)
                {
                    var rewrapped = await _rewrappingService.RewrapAuthenticatedAsync(envelope,
                        ArtifactEnvelopeContext.Create(request.ArtifactId), cancellationToken);
                    ValidateRewrappedEnvelope(envelope, rewrapped);
                    current = rewrapped.KeyEncryptionKeyId;
                    if (envelope.KeyEncryptionKeyId == current && envelope.WrappedDataEncryptionKey.AsSpan().SequenceEqual(rewrapped.WrappedDataEncryptionKey))
                        return await NonMutationAsync(intent with { CurrentKeyId = current }, ArtifactEnvelopeRewrappingOutcome.AlreadyCurrent, original: intent);
                    candidate = JsonSerializer.SerializeToUtf8Bytes(rewrapped);
                    await _staging.StageAsync(new(intent.OperationId.Value), candidate, cancellationToken);
                }
                else
                {
                    // Crash after staging, before journal acknowledgement. Authenticate the exact staged candidate;
                    // do not replace it with a newly randomized wrapping result.
                    var staged = ValidateEnvelope(JsonSerializer.Deserialize<EncryptedEnvelope>(candidate));
                    ValidateRewrappedEnvelope(envelope, staged);
                    await _rewrappingService.RewrapAuthenticatedAsync(staged, ArtifactEnvelopeContext.Create(request.ArtifactId), cancellationToken);
                    current = staged.KeyEncryptionKeyId;
                }
                var prepared = intent with { State = ArtifactRewrapState.Prepared, CandidateHash = Convert.ToHexString(SHA256.HashData(candidate)), CurrentKeyId = current };
                await _journal.UpdateAsync(intent, prepared, cancellationToken); intent = prepared;
            }
            current = intent.CurrentKeyId;
            cancellationToken.ThrowIfCancellationRequested();
            promotionStarted = true;
            return await PromoteAsync(intent, candidate!, cancellationToken);
        }
        catch (Exception error) when (!promotionStarted)
        {
            await WritePrePromotionAuditAsync(request, decision, error switch
            {
                OperationCanceledException => SecurityAuditOutcome.Cancelled,
                UnauthorizedAccessException => SecurityAuditOutcome.Denied,
                _ => SecurityAuditOutcome.Failed
            }, previous, current);
            throw;
        }
        catch (Exception) when (promotionStarted && intent is not null)
        {
            // An unavailable receipt/journal cannot justify replay or rollback under a new operation.
            try { observedReceipt ??= await _contentStore.GetMutationOutcomeAsync(new(intent.OperationId.Value), CancellationToken.None); } catch { }
            var known = observedReceipt?.ArtifactId == intent.ArtifactId && observedReceipt.OperationId.Value == intent.OperationId.Value &&
                observedReceipt.Kind == ArtifactContentMutationKind.Replace ? observedReceipt : null;
            return Result(intent, known, ArtifactAuditDeliveryState.RequiresReview);
        }
    }
    private async Task<ArtifactEnvelopeRewrappingResult> PromoteAsync(ArtifactRewrapIntent intent, byte[] candidate, CancellationToken ct)
    {
        ArtifactContentMutationReceipt? receipt;
        try
        {
            var mutation = await _contentStore.ReplaceIfRevisionMatchesAsync(intent.ArtifactId, intent.ExpectedRevision, candidate,
                new(new(intent.OperationId.Value), AuditEventId: new(intent.AuditEventId.Value), RequiresAuditObligation: true), ct);
            receipt = mutation.Receipt;
        }
        catch
        {
            receipt = await _contentStore.GetMutationOutcomeAsync(new(intent.OperationId.Value), CancellationToken.None);
            if (receipt is null) return await ReviewAsync(intent, "MutationAcknowledgementUnknown");
        }
        return await CompleteAsync(intent, receipt);
    }
    private Task<AuthorizationDecision> AuthorizeAsync(string subject, IArtifactMutationAuthorityLease lease, CancellationToken ct)
        => _authorizationPolicy.EvaluateAsync(new()
        {
            SubjectId = subject,
            PermissionId = SecurityPermissions.ArtifactEnvelopeRewrap,
            ResourceType = SecurityResourceTypes.Artifact,
            ResourceId = lease.ArtifactId.Value,
            ProtectionClassificationId = lease.ClassificationId
        }, ct);
    private async Task<ArtifactEnvelopeRewrappingResult> MissingIntentAsync(ArtifactEnvelopeRewrappingRequest request, ArtifactContentMutationReceipt receipt)
    {
        await _journal.RecordReviewAsync(new(request.OperationId, new(receipt.AuditEventId?.Value ?? "unknown"), request.ArtifactId,
            ArtifactRewrapState.RequiresReview, "MissingAuthorizedIntent"), CancellationToken.None);
        return new()
        {
            ArtifactId = request.ArtifactId,
            OperationId = request.OperationId,
            Outcome = receipt.ArtifactId == request.ArtifactId && receipt.Kind == ArtifactContentMutationKind.Replace ? Disposition(receipt) : ArtifactEnvelopeRewrappingOutcome.RequiresReview,
            CompletedUtc = receipt.OccurredUtc,
            AuditDelivery = ArtifactAuditDeliveryState.RequiresReview
        };
    }
    private static bool Matches(ArtifactRewrapIntent intent, ArtifactContentMutationReceipt receipt) =>
        receipt.OperationId.Value == intent.OperationId.Value && receipt.ArtifactId == intent.ArtifactId && receipt.Kind == ArtifactContentMutationKind.Replace &&
        receipt.AuditEventId?.Value == intent.AuditEventId.Value && receipt.PriorRevision == intent.ExpectedRevision &&
        receipt.Outcome is ArtifactContentMutationOutcome.Replaced or ArtifactContentMutationOutcome.VersionConflict or ArtifactContentMutationOutcome.Missing;
    private async Task<ArtifactEnvelopeRewrappingResult> CompleteAsync(ArtifactRewrapIntent intent, ArtifactContentMutationReceipt receipt)
    {
        // Conflict/Missing receipts can report the actual newer prior revision, never the stale expected revision.
        if (!(receipt.OperationId.Value == intent.OperationId.Value && receipt.ArtifactId == intent.ArtifactId &&
            receipt.Kind == ArtifactContentMutationKind.Replace && receipt.AuditEventId?.Value == intent.AuditEventId.Value &&
            (receipt.Outcome is ArtifactContentMutationOutcome.VersionConflict or ArtifactContentMutationOutcome.Missing || Matches(intent, receipt))))
            return await ReviewAsync(intent, "ReceiptIdentityConflict");
        var disposition = Disposition(receipt);
        var record = CreateEvent(intent, disposition, receipt.OccurredUtc);
        if (intent.Event is not null && !SecurityAuditCanonicalEvent.Encode(intent.Event).AsSpan().SequenceEqual(SecurityAuditCanonicalEvent.Encode(record)))
            return await ReviewAsync(intent, "OutcomeEventConflict");
        var pending = intent with { State = ArtifactRewrapState.CommittedAuditPending, Receipt = receipt, Event = record };
        try { if (intent.State != ArtifactRewrapState.Completed) await _journal.UpdateAsync(intent, pending, CancellationToken.None); }
        catch { return Result(pending, receipt, ArtifactAuditDeliveryState.Pending); }
        return await DeliverAsync(intent.State == ArtifactRewrapState.Completed ? intent : pending);
    }
    private async Task<ArtifactEnvelopeRewrappingResult> NonMutationAsync(ArtifactRewrapIntent intent,
        ArtifactEnvelopeRewrappingOutcome disposition, ArtifactRewrapIntent? original = null)
    {
        var pending = intent with { State = ArtifactRewrapState.CommittedAuditPending, Event = CreateEvent(intent, disposition, DateTimeOffset.UtcNow) };
        await _journal.UpdateAsync(original ?? intent, pending, CancellationToken.None); return await DeliverAsync(pending);
    }
    private async Task<ArtifactEnvelopeRewrappingResult> DeliverAsync(ArtifactRewrapIntent intent)
    {
        try
        {
            var ack = await _auditSink.AppendAsync(intent.Event ?? throw new InvalidDataException("Outcome event is missing."), CancellationToken.None);
            if (ack.EventId != intent.AuditEventId || ack.RecordId <= 0 || ack.RecordHash.Length != 64) return await ReviewAsync(intent, "AuditAcknowledgementConflict");
            if (intent.State != ArtifactRewrapState.Completed) await _journal.UpdateAsync(intent, intent with { State = ArtifactRewrapState.Completed }, CancellationToken.None);
            return Result(intent, intent.Receipt, ArtifactAuditDeliveryState.Completed);
        }
        catch (SecurityAuditIdentityConflictException) { return await ReviewAsync(intent, "AuditIdentityConflict"); }
        catch { return Result(intent, intent.Receipt, ArtifactAuditDeliveryState.Pending); }
    }
    private async Task<ArtifactEnvelopeRewrappingResult> ReviewAsync(ArtifactRewrapIntent intent, string category)
    {
        await _journal.RecordReviewAsync(new(intent.OperationId, intent.AuditEventId, intent.ArtifactId, ArtifactRewrapState.RequiresReview, category), CancellationToken.None);
        if (intent.State != ArtifactRewrapState.RequiresReview) await _journal.UpdateAsync(intent, intent with { State = ArtifactRewrapState.RequiresReview }, CancellationToken.None);
        return Result(intent, intent.Receipt, ArtifactAuditDeliveryState.RequiresReview);
    }
    private static ArtifactEnvelopeRewrappingOutcome Disposition(ArtifactContentMutationReceipt receipt) => receipt.Outcome switch
    {
        ArtifactContentMutationOutcome.Replaced => ArtifactEnvelopeRewrappingOutcome.Updated,
        ArtifactContentMutationOutcome.VersionConflict => ArtifactEnvelopeRewrappingOutcome.VersionConflict,
        ArtifactContentMutationOutcome.Missing => ArtifactEnvelopeRewrappingOutcome.NotFound,
        _ => ArtifactEnvelopeRewrappingOutcome.RequiresReview
    };
    private static ArtifactEnvelopeRewrappingResult Result(ArtifactRewrapIntent intent, ArtifactContentMutationReceipt? receipt, ArtifactAuditDeliveryState delivery)
        => new()
        {
            ArtifactId = intent.ArtifactId,
            OperationId = intent.OperationId,
            Outcome = receipt is not null ? Disposition(receipt) : intent.Event?.Facts.TryGetValue("disposition", out var value) == true ?
                Enum.Parse<ArtifactEnvelopeRewrappingOutcome>(value!) : ArtifactEnvelopeRewrappingOutcome.RequiresReview,
            PreviousKeyEncryptionKeyId = intent.PreviousKeyId,
            CurrentKeyEncryptionKeyId = intent.CurrentKeyId,
            CompletedUtc = receipt?.OccurredUtc ?? intent.Event?.OccurredUtc ?? intent.AuthorizedUtc,
            AuditDelivery = delivery
        };
    private static SecurityAuditRecord CreateEvent(ArtifactRewrapIntent intent, ArtifactEnvelopeRewrappingOutcome disposition, DateTimeOffset time)
    {
        var facts = KeyFacts(intent.PreviousKeyId, intent.CurrentKeyId);
        facts["classificationId"] = intent.ClassificationId.Value; facts["classificationRevision"] = intent.ClassificationRevision.Value;
        facts["disposition"] = disposition.ToString();
        return new()
        {
            AuditEventId = intent.AuditEventId,
            OperationId = intent.OperationId,
            OriginalActorId = intent.OriginalActorId,
            SubjectId = intent.OriginalActorId,
            Operation = SecurityPermissions.ArtifactEnvelopeRewrap.ToString(),
            ResourceType = SecurityResourceTypes.Artifact,
            ResourceId = intent.ArtifactId.Value,
            PolicyDecision = AuthorizationDecision.Allow,
            OccurredUtc = time,
            Facts = facts,
            Outcome = disposition switch
            {
                ArtifactEnvelopeRewrappingOutcome.Updated => SecurityAuditOutcome.Succeeded,
                ArtifactEnvelopeRewrappingOutcome.AlreadyCurrent => SecurityAuditOutcome.Skipped,
                _ => SecurityAuditOutcome.Failed
            }
        };
    }
    private Task WritePrePromotionAuditAsync(ArtifactEnvelopeRewrappingRequest request, AuthorizationDecision? decision,
        SecurityAuditOutcome outcome, string? previous, string? current) => _auditSink.AppendAsync(new()
        {
            AuditEventId = SecurityAuditEventId.New(),
            OperationId = request.OperationId,
            OriginalActorId = request.SubjectId,
            SubjectId = request.SubjectId,
            Operation = SecurityPermissions.ArtifactEnvelopeRewrap.ToString(),
            ResourceType = SecurityResourceTypes.Artifact,
            ResourceId = request.ArtifactId.Value,
            PolicyDecision = decision,
            Outcome = outcome,
            OccurredUtc = DateTimeOffset.UtcNow,
            Facts = KeyFacts(previous, current)
        }, CancellationToken.None);
    private static Dictionary<string, string> KeyFacts(string? previous, string? current)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (previous is not null) facts["previousKeyEncryptionKeyId"] = previous;
        if (current is not null) facts["currentKeyEncryptionKeyId"] = current; return facts;
    }
    private async Task RecordRecoveryReviewAsync(SecurityMutationOperationId operationId, SecurityAuditEventId requiredEvent,
        EMF.Core.Models.Identities.ArtifactId artifactId, string recoveryActor, string category, CancellationToken ct)
    {
        var prior = await _journal.ReadReviewAsync(operationId, ct);
        var recoveryEvent = prior?.RecoveryEvent ?? new SecurityAuditRecord
        {
            AuditEventId = SecurityAuditEventId.New(),
            OperationId = operationId,
            OriginalActorId = recoveryActor,
            ServiceActorId = recoveryActor,
            RecoveryActorId = recoveryActor,
            SubjectId = recoveryActor,
            Operation = SecurityPermissions.ArtifactEnvelopeRewrap.ToString(),
            ResourceType = SecurityResourceTypes.Artifact,
            ResourceId = artifactId.Value,
            PolicyDecision = AuthorizationDecision.Allow,
            Outcome = SecurityAuditOutcome.Failed,
            OccurredUtc = DateTimeOffset.UtcNow,
            Facts = new Dictionary<string, string> { { "recoveryAction", category }, { "disposition", ArtifactEnvelopeRewrappingOutcome.RequiresReview.ToString() } }
        };
        await _journal.RecordReviewAsync(new(operationId, requiredEvent, artifactId, ArtifactRewrapState.RequiresReview, category, recoveryEvent), ct);
        var stable = await _journal.ReadReviewAsync(operationId, ct);
        try { await _auditSink.AppendAsync(stable!.RecoveryEvent!, ct); }
        catch (Exception error) when (error is not OperationCanceledException) { /* Durable review work survives chain outage. */ }
    }
    // Independent receipt inventory: journal loss never erases a required audit obligation.
    public async Task<ArtifactContentReceiptCursor?> ReconcileAsync(string recoveryActor,
        ArtifactContentReceiptCursor? afterCursor = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        var obligations = await _contentStore.ReadAuditObligationsAsync(afterCursor, limit, cancellationToken);
        foreach (var obligation in obligations)
        {
            var receipt = obligation.Receipt;
            await using var authority = await _authority.AcquireAsync(receipt.ArtifactId, cancellationToken);
            if (!authority.IsAdopted || await AuthorizeAsync(recoveryActor, authority, cancellationToken) != AuthorizationDecision.Allow)
                throw new UnauthorizedAccessException("Rewrap reconciliation is denied.");
            var op = new SecurityMutationOperationId(receipt.OperationId.Value); var eventId = new SecurityAuditEventId(receipt.AuditEventId!.Value.Value);
            ArtifactRewrapIntent? intent;
            try { intent = await _journal.ReadAsync(op, cancellationToken); }
            catch (InvalidDataException) { intent = null; }
            try
            {
                var acknowledged = await _auditSink.FindVerifiedAsync(eventId, cancellationToken);
                if (intent is not null) { await CompleteAsync(intent, receipt); continue; }
                var record = acknowledged?.Record;
                var disposition = Disposition(receipt);
                if (record is not null && record.OperationId == op && record.ResourceId == receipt.ArtifactId.Value &&
                    record.Operation == SecurityPermissions.ArtifactEnvelopeRewrap.ToString() && record.ResourceType == SecurityResourceTypes.Artifact &&
                    record.PolicyDecision == AuthorizationDecision.Allow && record.RecoveryActorId is null && record.SubjectId == record.OriginalActorId &&
                    record.OccurredUtc == receipt.OccurredUtc &&
                    record.Facts.TryGetValue("disposition", out var actual) && actual == disposition.ToString() &&
                    record.Outcome == (disposition == ArtifactEnvelopeRewrappingOutcome.Updated ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Failed))
                    continue;
                await RecordRecoveryReviewAsync(op, eventId, receipt.ArtifactId, recoveryActor,
                    acknowledged is null ? "MissingAuthorizedIntent" : "CanonicalAcknowledgementConflict", cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                await RecordRecoveryReviewAsync(op, eventId, receipt.ArtifactId, recoveryActor, "AuditChainUnknown", cancellationToken);
            }
        }
        return obligations.Count == 0 ? afterCursor : obligations[^1].Cursor;
    }
    private static EncryptedEnvelope ValidateEnvelope(
        EncryptedEnvelope? envelope)
    {
        if (envelope is null ||
            envelope.Ciphertext is null ||
            envelope.Nonce is null ||
            envelope.Nonce.Length == 0 ||
            envelope.AuthenticationTag is null ||
            envelope.AuthenticationTag.Length == 0 ||
            envelope.WrappedDataEncryptionKey is null ||
            envelope.WrappedDataEncryptionKey.Length == 0 ||
            string.IsNullOrWhiteSpace(
                envelope.KeyEncryptionKeyId) ||
            string.IsNullOrWhiteSpace(
                envelope.Algorithm))
        {
            throw new InvalidOperationException(
                "Encrypted artifact envelope is invalid.");
        }

        try
        {
            EncryptedEnvelopeFormat.Validate(
                envelope);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "Encrypted artifact envelope is invalid.",
                exception);
        }

        return envelope;
    }

    private static void ValidateRewrappedEnvelope(
        EncryptedEnvelope original,
        EncryptedEnvelope replacement)
    {
        ValidateEnvelope(replacement);

        if (original.FormatVersion !=
                replacement.FormatVersion ||
            !original.Ciphertext.SequenceEqual(
                replacement.Ciphertext) ||
            !original.Nonce.SequenceEqual(
                replacement.Nonce) ||
            !original.AuthenticationTag.SequenceEqual(
                replacement.AuthenticationTag) ||
            original.Algorithm != replacement.Algorithm)
        {
            throw new InvalidOperationException(
                "Rewrapping changed protected content metadata.");
        }
    }

}
