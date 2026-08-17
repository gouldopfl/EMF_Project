using EMF.Core.Contracts;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Models;

namespace EMF.Security;

public sealed class WorkflowActivityClaimRecoveryService :
    IWorkflowActivityClaimRecoveryService
{
    private readonly IWorkflowRepository _repository;
    private readonly IAuthorizationPolicy _authorization;
    private readonly ISecurityAuditSink _audit;

    public WorkflowActivityClaimRecoveryService(
        IWorkflowRepository repository,
        IAuthorizationPolicy authorization,
        ISecurityAuditSink audit)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(audit);

        _repository = repository;
        _authorization = authorization;
        _audit = audit;
    }

    public async Task<bool> RecoverAsync(
        WorkflowActivityClaimRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NewClaimId);

        var decision = await _authorization.EvaluateAsync(
            new AuthorizationRequest
            {
                SubjectId = request.SubjectId,
                PermissionId =
                    SecurityPermissions.WorkflowActivityClaimRecover,
                ResourceType = SecurityResourceTypes.Workflow,
                ResourceId = request.WorkflowId.Value,
                ProtectionClassificationId =
                    request.ProtectionClassificationId
            },
            cancellationToken);

        if (decision != AuthorizationDecision.Allow)
        {
            await WriteAuditAsync(
                request, decision,
                SecurityAuditOutcome.Denied);

            throw new UnauthorizedAccessException(
                "Workflow activity claim recovery was denied.");
        }

        try
        {
            var recovered =
                await _repository.TryReclaimActivityAsync(
                    request.WorkflowId,
                    request.ActivityId,
                    request.NewClaimId,
                    request.ReclaimedUtc,
                    request.AbandonedBeforeUtc,
                    cancellationToken);

            await WriteAuditAsync(
                request, decision,
                recovered
                    ? SecurityAuditOutcome.Succeeded
                    : SecurityAuditOutcome.Skipped);

            return recovered;
        }
        catch (OperationCanceledException)
        {
            await WriteAuditAsync(
                request, decision,
                SecurityAuditOutcome.Cancelled);

            throw;
        }
        catch
        {
            await WriteAuditAsync(
                request, decision,
                SecurityAuditOutcome.Failed);

            throw;
        }
    }

    private Task WriteAuditAsync(
        WorkflowActivityClaimRecoveryRequest request,
        AuthorizationDecision decision,
        SecurityAuditOutcome outcome)
    {
        return _audit.WriteAsync(
            new SecurityAuditRecord
            {
                Operation = SecurityPermissions
                    .WorkflowActivityClaimRecover.Value,
                ResourceType = SecurityResourceTypes.Workflow,
                ResourceId = request.WorkflowId.Value,
                SubjectId = request.SubjectId,
                PolicyDecision = decision,
                Outcome = outcome,
                OccurredUtc = DateTimeOffset.UtcNow,
                Facts = new Dictionary<string, string>
                {
                    ["activityId"] = request.ActivityId,
                    ["newClaimId"] = request.NewClaimId,
                    ["abandonedBeforeUtc"] =
                        request.AbandonedBeforeUtc.ToString("O")
                }
            },
            CancellationToken.None);
    }
}
