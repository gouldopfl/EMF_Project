namespace EMF.Core.Contracts.Zip;

// A scoped permission for one acknowledged occurrence and its exact retained copy.
// The ACK and mutation receipts are evidence, never executable authority.
public sealed record ZipProtectedReleaseEvidence(ZipParentSnapshot Parent, ZipEntryProgress Entry,
    ZipRetentionRecord Retention, ZipAcknowledgementBinding Acknowledgement,
    long ContainsId, long DerivedFromId);

public sealed record ZipProtectedReleaseAuthorityRequest(ZipParentAdmission Admission,
    ZipParentBinding Parent, ZipFence Fence, ZipEntryPlan Occurrence, ZipRetainedBinding ChildInput,
    ZipChildIngestionProof Ingestion, ZipAcknowledgementBinding Acknowledgement,
    long ContainsId, long DerivedFromId, ZipRetentionRecord Retention,
    string ExecutingActorId, ZipOwnershipPurpose Purpose, DateTimeOffset GrantValidUntilUtc);
