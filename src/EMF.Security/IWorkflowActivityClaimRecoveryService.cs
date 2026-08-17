using EMF.Security.Models;

namespace EMF.Security;

public interface IWorkflowActivityClaimRecoveryService
{
    Task<bool> RecoverAsync(
        WorkflowActivityClaimRecoveryRequest request,
        CancellationToken cancellationToken = default);
}
