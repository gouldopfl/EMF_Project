using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IWorkflowRunner
{
    Task ExecuteAsync(
        WorkflowExecutionContext context,
        IEnumerable<IWorkflowActivity> activities,
        string? retryActivityId = null,
        OperationId? retryOperationId = null,
        CancellationToken cancellationToken = default);
}
