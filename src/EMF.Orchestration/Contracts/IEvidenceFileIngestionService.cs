using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IEvidenceFileIngestionService
{
    Task<EvidenceFileIngestionResult> IngestAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
