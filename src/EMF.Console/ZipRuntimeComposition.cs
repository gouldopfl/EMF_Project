using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Orchestration.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;
namespace EMF.ConsoleApplication;

public sealed class ZipRuntimeComposition
{
    public static ZipRuntimeComposition Default { get; } = new();
    public ZipParentAllocationProfile Profile { get; }
    public ZipParentReadAdmission Admission => ZipParentReadAdmission.ProcessWide;
    public IZipChildIngestionHost? ChildIngestionHost { get; }
    // A child ingestion host alone cannot authorize parent-operation admission.
    // The stock workflow has no such authority and must never select the legacy
    // direct publisher as a substitute for the governed durable driver.
    public IZipArchiveWorkflowActivity CreateWorkflowActivity(IEvidenceRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return new UnavailableDurableWorkflowActivity(repository, ChildIngestionHost is not null);
    }
    private sealed class UnavailableDurableWorkflowActivity(IEvidenceRepository repository, bool hasChildHost)
        : IZipArchiveWorkflowActivity
    {
        public string Id => "zip-archives";
        public string Name => "ZIP Archives";
        public async Task<WorkflowActivityResult> ExecuteAsync(WorkflowExecutionContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            var archives = await repository.GetArtifactsByMetadataAsync(ArtifactMetadataKeys.FileExtension, ".zip", cancellationToken);
            return new WorkflowActivityResult
            {
                Succeeded = archives.Count == 0,
                Message = archives.Count == 0
                    ? "No ZIP archives require extraction."
                    : hasChildHost
                        ? "ZIP extraction requires authoritative parent-operation workflow composition. Review is required."
                        : "ZIP extraction requires authoritative durable workflow composition. Review is required.",
                CompletedUtc = DateTimeOffset.UtcNow
            };
        }
    }
    public ZipRuntimeComposition(ZipParentAllocationProfile? profile = null, IZipChildIngestionHost? childIngestionHost = null)
    { Profile = profile ?? new(); ChildIngestionHost = childIngestionHost; }
}
