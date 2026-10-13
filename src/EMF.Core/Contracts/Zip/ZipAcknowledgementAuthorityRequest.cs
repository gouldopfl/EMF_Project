namespace EMF.Core.Contracts.Zip;

// One live permission for one already ingested, exact ZIP occurrence. The
// receipt and relationship rows remain durable facts, never authority.
public sealed record ZipAcknowledgementAuthorityRequest(
    ZipParentAdmission Admission, ZipParentBinding Parent, ZipFence Fence,
    ZipEntryPlan Occurrence, ZipRetainedBinding ChildInput,
    ZipChildIngestionProof Ingestion, string ExecutingActorId,
    ZipOwnershipPurpose Purpose, DateTimeOffset GrantValidUntilUtc);
