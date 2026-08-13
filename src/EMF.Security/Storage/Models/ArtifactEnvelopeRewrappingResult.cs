using EMF.Core.Models.Identities;

namespace EMF.Security.Storage.Models;

public sealed class ArtifactEnvelopeRewrappingResult
{
    public required ArtifactId ArtifactId { get; init; }

    public required ArtifactEnvelopeRewrappingOutcome
        Outcome
    { get; init; }

    public string? PreviousKeyEncryptionKeyId { get; init; }

    public string? CurrentKeyEncryptionKeyId { get; init; }

    public required DateTimeOffset CompletedUtc { get; init; }
}
