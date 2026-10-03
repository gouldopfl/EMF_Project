using EMF.Core.Models;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Storage;

namespace EMF.Orchestration.Models;

public sealed class EvidenceFileIngestionResult
{
    public required Artifact Artifact { get; init; }

    public required Provenance Provenance { get; init; }

    public bool AlreadyExisted { get; init; }
    public ArtifactContentOperationId OperationId { get; init; }
    public ArtifactIngestionState LifecycleState { get; init; }
    public bool IsAdopted { get; init; }
    public IngestionAuditDelivery AuditDelivery { get; init; }
}
