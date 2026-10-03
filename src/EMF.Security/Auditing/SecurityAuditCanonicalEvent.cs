using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Models;
namespace EMF.Security.Auditing;

public static class SecurityAuditCanonicalEvent
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly HashSet<string> RewrapFacts = new(StringComparer.Ordinal)
    { "previousKeyEncryptionKeyId", "currentKeyEncryptionKeyId", "classificationId", "classificationRevision", "disposition", "recoveryAction" };
    private static readonly HashSet<string> IngestionFacts = new(StringComparer.Ordinal)
    { "classificationId", "classificationRevision", "disposition", "recoveryCondition", "ingestionSchemaVersion" };
    // Frozen ingestion fact schema 1. Additional conditions require a new schema
    // version and an explicit compatibility path; expanding an enum cannot reinterpret v1.
    private static readonly HashSet<string> IngestionConditionsV1 = new(StringComparer.Ordinal)
    {
        "AdoptionClassificationMismatch", "AuditEvidenceFailure", "AuditIdentityConflict", "AuditJournalDamage",
        "CandidateIntegrityFailure", "CanonicalReconciliationFailure", "CleanupAuthorityFailure", "CleanupAuthorizationUnavailable",
        "CleanupOutcomeUnknown", "CleanupReceiptConflict", "ContradictoryAdoption", "CreationEvidenceFailure",
        "CreationOutcomeUnknown", "CreationReceiptConflict", "DeliveryRecoveryPending", "LifecycleDamage",
        "ProvisionalAuthorityFailure", "ReceiptAuditConflict", "ReceiptWithoutValidIntent", "RecoveryAuthorizationDenied",
        "RecoveryEvidenceFailure", "UnexpectedPhysicalContent"
    };
    public static string FormatTime(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    public static SecurityAuditRecord Freeze(SecurityAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record); ArgumentNullException.ThrowIfNull(record.Facts);
        if (record.Facts.Count > RewrapFacts.Count) throw new ArgumentException("Too many audit facts.");
        return new()
        {
            AuditEventId = record.AuditEventId,
            OperationId = record.OperationId,
            OriginalActorId = record.OriginalActorId,
            ServiceActorId = record.ServiceActorId,
            RecoveryActorId = record.RecoveryActorId,
            Operation = record.Operation,
            ResourceType = record.ResourceType,
            ResourceId = record.ResourceId,
            SubjectId = record.SubjectId,
            PolicyDecision = record.PolicyDecision,
            Destination = record.Destination,
            Outcome = record.Outcome,
            OccurredUtc = record.OccurredUtc,
            Facts = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(record.Facts.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal))
        };
    }
    public static byte[] Encode(SecurityAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        SecurityAuditIdentity.Validate(record.AuditEventId?.Value ?? throw new ArgumentException("Audit event identity is required."));
        if (record.OperationId is { } operationId) SecurityAuditIdentity.Validate(operationId.Value);
        Actor(record.OriginalActorId, true); Actor(record.SubjectId, true);
        Actor(record.ServiceActorId, false); Actor(record.RecoveryActorId, false);
        if (record.SubjectId != (record.RecoveryActorId ?? record.ServiceActorId ?? record.OriginalActorId))
            throw new ArgumentException("Audit actor roles are inconsistent.");
        if (!Enum.IsDefined(record.Outcome) || record.PolicyDecision is { } p && !Enum.IsDefined(p))
            throw new ArgumentException("Audit enum is invalid.");
        var ingestion = record.Operation == SecurityPermissions.ArtifactIngest.Value;
        if ((!ingestion && record.Operation != SecurityPermissions.ArtifactEnvelopeRewrap.ToString()) ||
            record.ResourceType != SecurityResourceTypes.Artifact || record.OperationId is null)
            throw new ArgumentException("No approved canonical fact schema for this operation.");
        if (string.IsNullOrWhiteSpace(record.ResourceId) || Utf8.GetByteCount(record.ResourceId) > 128 || record.ResourceId.Any(char.IsControl))
            throw new ArgumentException("Artifact identity is invalid.");
        ArgumentNullException.ThrowIfNull(record.Facts);
        var approvedFacts = ingestion ? IngestionFacts : RewrapFacts;
        if (record.Facts.Count > approvedFacts.Count) throw new ArgumentException("Too many facts.");
        if (ingestion && (!record.Facts.TryGetValue("ingestionSchemaVersion", out var ingestionVersion) || ingestionVersion != "1"))
            throw new ArgumentException("Unsupported ingestion canonical fact schema version.");
        if (ingestion)
        {
            foreach (var key in new[] { "classificationId", "classificationRevision", "disposition" })
                if (!record.Facts.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Missing required ingestion canonical fact.");
            if ((record.Facts["disposition"] == "RequiresReview") != record.Facts.ContainsKey("recoveryCondition"))
                throw new ArgumentException("Ingestion review condition does not match the disposition.");
        }
        using var stream = new MemoryStream();
        Scalar(stream, "EMF-SECURITY-AUDIT-EVENT-V2");
        foreach (var value in new[] { record.AuditEventId.Value.Value, record.OperationId?.Value,
            record.OriginalActorId, record.ServiceActorId, record.RecoveryActorId, record.Operation,
            record.ResourceType, record.ResourceId, record.SubjectId, record.PolicyDecision?.ToString(),
            record.Destination, record.Outcome.ToString(), FormatTime(record.OccurredUtc) }) Scalar(stream, value);
        Integer(stream, record.Facts.Count);
        // Approved keys are ASCII, so ordinal order is identical to strict UTF-8 byte order.
        foreach (var fact in record.Facts.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!approvedFacts.Contains(fact.Key) || fact.Value is null) throw new ArgumentException("Unapproved audit fact.");
            if (fact.Value.Any(char.IsControl)) throw new ArgumentException("Invalid fact characters.");
            if (ingestion && fact.Key == "recoveryCondition" && !IngestionConditionsV1.Contains(fact.Value))
                throw new ArgumentException("Unapproved ingestion recovery condition.");
            if (ingestion && fact.Key == "disposition" && (!Enum.TryParse<EMF.Core.Contracts.Ingestion.IngestionAuditAction>(fact.Value, false, out var ingestionAction)
                || !Enum.IsDefined(ingestionAction) || ingestionAction.ToString() != fact.Value)) throw new ArgumentException("Invalid ingestion disposition.");
            if (!ingestion && fact.Key == "disposition" && (!Enum.TryParse<Storage.Models.ArtifactEnvelopeRewrappingOutcome>(fact.Value, false, out var disposition) ||
                !Enum.IsDefined(disposition) || disposition.ToString() != fact.Value)) throw new ArgumentException("Invalid disposition fact.");
            if (fact.Key is "classificationId" or "classificationRevision" && Utf8.GetByteCount(fact.Value) > 128)
                throw new ArgumentException("Classification fact exceeds bound.");
            Scalar(stream, fact.Key); Scalar(stream, fact.Value);
        }
        return stream.ToArray();
    }
    public static string Hash(string? previous, ReadOnlySpan<byte> canonical)
    {
        using var stream = new MemoryStream();
        Scalar(stream, "2"); Scalar(stream, previous); Integer(stream, canonical.Length); stream.Write(canonical);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
    private static void Actor(string? value, bool required)
    {
        if (value is null) { if (required) throw new ArgumentException("Actor identity is required."); return; }
        if (string.IsNullOrWhiteSpace(value) || Utf8.GetByteCount(value) > 256) throw new ArgumentException("Actor identity is invalid.");
    }
    private static void Scalar(Stream stream, string? value)
    {
        if (value is null) { Integer(stream, -1); return; }
        var bytes = Utf8.GetBytes(value);
        if (bytes.Length > 1024) throw new ArgumentException("Audit scalar exceeds its bound.");
        Integer(stream, bytes.Length); stream.Write(bytes);
    }
    private static void Integer(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); stream.Write(bytes);
    }
}
