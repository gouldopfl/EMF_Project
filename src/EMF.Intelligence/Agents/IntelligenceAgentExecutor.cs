using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;

namespace EMF.Intelligence.Agents;

public sealed class IntelligenceAgentExecutor<
    TObjective,
    TResult> :
    IIntelligenceAgentExecutor<
        TObjective,
        TResult>
    where TObjective : notnull
    where TResult : notnull
{
    private readonly IntelligenceAgentRegistry<
        TObjective,
        TResult> _registry;

    private readonly IntelligenceAgentAuditWriter
        _auditWriter;

    public IntelligenceAgentExecutor(
        IntelligenceAgentRegistry<
            TObjective,
            TResult> registry,
        ISecurityAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(auditSink);

        _registry = registry;
        _auditWriter =
            new IntelligenceAgentAuditWriter(
                auditSink);
    }

    public async Task<
        IntelligenceAgentResult<TResult>>
        ExecuteAsync(
            AgentId agentId,
            TObjective objective,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            agentId.Value);

        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(context);

        if (!context.AgentId.HasValue ||
            context.AgentId.Value != agentId)
        {
            throw new ArgumentException(
                "Execution context Agent ID must " +
                "match the requested agent.",
                nameof(context));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await _auditWriter.WriteAsync<TResult>(
                agentId,
                context,
                null,
                SecurityAuditOutcome.Cancelled,
                DateTimeOffset.UtcNow);

            cancellationToken.ThrowIfCancellationRequested();
        }

        IntelligenceAgentResult<TResult>? result = null;

        try
        {
            var agent =
                _registry.Resolve(agentId);

            result =
                await agent.ExecuteAsync(
                    objective,
                    context,
                    cancellationToken);

            IntelligenceAgentResultValidator.Validate(
                result,
                agentId,
                context);
        }
        catch (OperationCanceledException)
        {
            await _auditWriter.WriteAsync(
                agentId,
                context,
                result,
                SecurityAuditOutcome.Cancelled,
                DateTimeOffset.UtcNow);

            throw;
        }
        catch (Exception)
        {
            await _auditWriter.WriteAsync(
                agentId,
                context,
                result,
                SecurityAuditOutcome.Failed,
                DateTimeOffset.UtcNow);

            throw;
        }

        await _auditWriter.WriteAsync(
            agentId,
            context,
            result,
            result.Success
                ? SecurityAuditOutcome.Succeeded
                : SecurityAuditOutcome.Failed,
            DateTimeOffset.UtcNow);

        return result;
    }
}
