using EMF.Core.Models.Identities;
using System.Text.Json.Serialization;

namespace EMF.Core.Contracts.Storage;

// Workflow OperationId has workflow replay semantics. Content mutations use
// their own bounded identity; no Security-owned type crosses this boundary.
public static class ArtifactContentIdentity
{
    public static string Validate(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 ||
            value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':')))
            throw new ArgumentException("Content identity must be 1–128 bounded ASCII characters.");
        return value;
    }
}
public readonly record struct ArtifactContentRevision
{
    public string Value { get; }
    [JsonConstructor]
    public ArtifactContentRevision(string value) => Value = ArtifactContentIdentity.Validate(value);
}
public readonly record struct ArtifactContentOperationId
{
    public string Value { get; }
    [JsonConstructor]
    public ArtifactContentOperationId(string value) => Value = ArtifactContentIdentity.Validate(value);
    public static ArtifactContentOperationId New() => new(Guid.NewGuid().ToString("N"));
}
public readonly record struct ArtifactContentOwnershipToken
{
    public string Value { get; }
    [JsonConstructor]
    public ArtifactContentOwnershipToken(string value) => Value = ArtifactContentIdentity.Validate(value);
}
public readonly record struct ArtifactContentAuditEventId
{
    public string Value { get; }
    [JsonConstructor]
    public ArtifactContentAuditEventId(string value) => Value = ArtifactContentIdentity.Validate(value);
}
public sealed record ArtifactContentMutationContext(
    ArtifactContentOperationId OperationId,
    ArtifactContentOwnershipToken? OwnershipToken = null,
    ArtifactContentAuditEventId? AuditEventId = null,
    bool RequiresAuditObligation = false);
public enum ArtifactContentMutationKind { Create, Replace, Delete, LegacyWrite, LegacyDelete }
public enum ArtifactContentMutationOutcome { Created, Replaced, Deleted, AlreadyExists, VersionConflict, Missing, Unsupported }
public sealed record ArtifactContentSnapshot(byte[] Content, ArtifactContentRevision Revision);
public sealed record ArtifactContentMutationReceipt(
    ArtifactContentOperationId OperationId, ArtifactId ArtifactId,
    ArtifactContentMutationKind Kind, ArtifactContentRevision? PriorRevision,
    ArtifactContentRevision? CurrentRevision, ArtifactContentOwnershipToken? OwnershipToken,
    ArtifactContentMutationOutcome Outcome, DateTimeOffset OccurredUtc,
    ArtifactContentAuditEventId? AuditEventId, int AuditObligationVersion = 1);
public sealed record ArtifactContentMutationResult(ArtifactContentMutationReceipt Receipt)
{
    public ArtifactContentMutationOutcome Outcome => Receipt.Outcome;
    public ArtifactContentRevision? PriorRevision => Receipt.PriorRevision;
    public ArtifactContentRevision? CurrentRevision => Receipt.CurrentRevision;
}
public sealed class ArtifactContentIdempotencyException : InvalidOperationException
{
    public ArtifactContentIdempotencyException() : base("Content mutation operation identity conflicts with the original request.") { }
}

// A store-scoped durable forward position, distinct from logical mutation,
// physical revision, lifecycle ownership and canonical audit-event identities.
public readonly record struct ArtifactContentReceiptCursor
{
    public long Value { get; }
    public ArtifactContentReceiptCursor(long value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }
}
public sealed record ArtifactContentAuditObligation(
    ArtifactContentReceiptCursor Cursor, ArtifactContentMutationReceipt Receipt);
