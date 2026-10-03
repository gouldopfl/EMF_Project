using EMF.Core.Models.Identities;
using EMF.Security.Models.Identities;
namespace EMF.Security.Storage;

public readonly record struct ArtifactClassificationRevision
{
    public string Value { get; }
    [System.Text.Json.Serialization.JsonConstructor]
    public ArtifactClassificationRevision(string value) => Value = Auditing.Models.SecurityAuditIdentity.Validate(value);
}
// The authority adapter must fence classification/adoption writers for this lease.
// Unknown classification or unadopted provisional resources cannot yield an ordinary mutation lease.
public interface IArtifactMutationAuthority
{
    Task<IArtifactMutationAuthorityLease> AcquireAsync(ArtifactId artifactId, CancellationToken cancellationToken = default);
}
public interface IArtifactMutationAuthorityLease : IAsyncDisposable
{
    ArtifactId ArtifactId { get; }
    ProtectionClassificationId ClassificationId { get; }
    ArtifactClassificationRevision ClassificationRevision { get; }
    bool IsAdopted { get; }
}
