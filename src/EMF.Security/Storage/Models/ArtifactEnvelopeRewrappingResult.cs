using EMF.Core.Models.Identities;

namespace EMF.Security.Storage.Models;

public sealed class ArtifactEnvelopeRewrappingResult
{
    public Auditing.Models.SecurityMutationOperationId OperationId { get; init; }
    public ArtifactAuditDeliveryState AuditDelivery { get; init; }
    public required ArtifactId ArtifactId { get; init; }

    public required ArtifactEnvelopeRewrappingOutcome
        Outcome
    { get; init; }

    public string? PreviousKeyEncryptionKeyId { get; init; }

    public string? CurrentKeyEncryptionKeyId { get; init; }

    public required DateTimeOffset CompletedUtc { get; init; }
}

public enum ArtifactAuditDeliveryState { Pending, Completed, RequiresReview }
