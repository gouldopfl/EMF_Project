using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Zip;
using EMF.Security.Ingestion;
namespace EMF.Orchestration.Contracts;
// Authenticated host composition supplies authority; entry metadata never supplies it.
public interface IZipChildIngestionHost
{
    Task<ZipChildIngestionRuntime> OpenAsync(ZipParentSnapshot parent,ZipEntryProgress entry,CancellationToken ct=default);
}
public sealed record ZipChildIngestionRuntime(ArtifactIngestionCoordinator Coordinator,IArtifactIngestionPersistence Persistence);
