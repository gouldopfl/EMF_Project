using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class DevelopEvidenceGapWorkflowActivity :
    IWorkflowActivity
{
    private readonly IEvidenceGapRepository _repository;
    private readonly EvidenceGapId _evidenceGapId;

    public DevelopEvidenceGapWorkflowActivity(
        IEvidenceGapRepository repository,
        EvidenceGapId evidenceGapId)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
        _evidenceGapId = evidenceGapId;
    }

    public string Id => "develop-evidence-gap";

    public string Name => "Develop Evidence Gap";

    public async Task<WorkflowActivityResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var gap =
            await _repository.GetEvidenceGapAsync(
                _evidenceGapId,
                cancellationToken);

        return new WorkflowActivityResult
        {
            Succeeded = gap is not null,
            Message = gap is null
                ? "Evidence gap was not found."
                : gap.Description,
            CompletedUtc = DateTimeOffset.UtcNow
        };
    }
}
