using System.Collections.ObjectModel;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed record ReviewerConsumptionAuthorizationMember(string ArtifactId,
    string ClassificationId, string ClassificationRevision);

// A copied, bounded policy view of validated capture facts. No lifecycle owner
// credential, mutable manifest, or plaintext is exposed to the policy provider.
public sealed class ReviewerConsumptionAuthorizationRequest
{
    public const int MaximumMembers = 4096;
    public ReviewerOperationId ReviewerOperationId { get; }
    public OperationSnapshotId SnapshotId { get; }
    public string Profile { get; }
    public int RepresentationVersion { get; }
    public ReadOnlyCollection<ReviewerConsumptionAuthorizationMember> Members { get; }

    public ReviewerConsumptionAuthorizationRequest(ReviewerOperationId reviewerOperationId,
        OperationSnapshotId snapshotId, string profile, int representationVersion,
        IEnumerable<ReviewerConsumptionAuthorizationMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var copy = members.Take(MaximumMembers + 1).ToArray();
        if (copy.Length is 0 or > MaximumMembers || copy.Any(member => member is null ||
            string.IsNullOrWhiteSpace(member.ArtifactId) || string.IsNullOrWhiteSpace(member.ClassificationId) ||
            string.IsNullOrWhiteSpace(member.ClassificationRevision)))
            throw new InvalidDataException("Authorization member facts are missing or exceed the bounded policy view.");
        ReviewerOperationId = reviewerOperationId;
        SnapshotId = snapshotId;
        Profile = profile;
        RepresentationVersion = representationVersion;
        Members = Array.AsReadOnly(copy);
    }
}
