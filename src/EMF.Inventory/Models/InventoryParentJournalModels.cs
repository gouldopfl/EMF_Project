namespace EMF.Inventory.Models;

public enum InventoryMode { MetadataOnly, ProtectedContent }
public enum InventoryConfirmationDisposition { MetadataPersisted, ContentAdopted, CanonicalDeduplicated }
public enum InventoryParentStatus { Active, Completed, RequiresReview }
public enum InventoryChildStatus { Planned, Started, Retryable, Rejected, Confirmed }
public enum InventoryRetentionStatus { Reserved, Sealing, Sealed, ReleasePending, Released }

// These are Inventory values, not authorization grants or Core identity aliases.
public sealed record InventoryAuthorityBinding(string AuthorityId, string Revision, string ActorId);
public sealed record InventoryRetainedBinding(string ObjectId, string Representation, string Revision,
    string Fingerprint, long Length, string OwnerToken, string CreateOperationId, string ReleaseOperationId);
public sealed record InventoryRetentionRecord(string ParentId, string ObjectId, string OwnerToken,
    string CreateOperationId, string ReleaseOperationId, InventoryRetentionStatus Status,
    string? Fingerprint = null, long Length = 0, InventoryRetainedBinding? Binding = null);
public sealed record InventoryPlanItem(int Ordinal, string ChildOperationId, string ArtifactId,
    string SourceLocator, InventoryRetainedBinding Retained, string DraftJson);
public sealed record InventoryParentPlan(string ParentId, string WorkflowId, string WorkflowOperationId,
    InventoryMode Mode, InventoryAuthorityBinding? Authority, IReadOnlyList<InventoryPlanItem> Items);
public sealed record InventoryParentState(InventoryParentPlan Plan, string OwnerToken, long OwnerEpoch,
    long Version, int ConfirmedOrdinal, InventoryParentStatus Status);
public sealed record InventoryChildConfirmation(int Ordinal, string ChildOperationId,
    InventoryConfirmationDisposition Disposition, string CanonicalArtifactId, string Evidence);

public static class InventoryIdentity
{
    public static string New() => Guid.NewGuid().ToString("N");
    public static string Validate(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 ||
            value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':')))
            throw new ArgumentException("Invalid Inventory identity.");
        return value;
    }
}
