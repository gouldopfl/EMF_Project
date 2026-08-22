using EMF.Intelligence.Agents;
using EMF.Intelligence.Models;
using EMF.Security.Auditing;

namespace EMF.Intelligence.State;

public sealed class StatefulIntelligenceAgentExecutor<
    TObjective,
    TResult>
    where TObjective : notnull
    where TResult : notnull
{
    private readonly
        IStatefulIntelligenceAgent<
            TObjective,
            TResult> _agent;

    private readonly
        IIntelligenceAgentStateStore _stateStore;

    private readonly
        IntelligenceAgentAuditWriter _auditWriter;

    public StatefulIntelligenceAgentExecutor(
        IStatefulIntelligenceAgent<
            TObjective,
            TResult> agent,
        IIntelligenceAgentStateStore stateStore,
        ISecurityAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(auditSink);

        _agent = agent;
        _stateStore = stateStore;
        _auditWriter =
            new IntelligenceAgentAuditWriter(
                auditSink);
    }

    public async Task<
        StatefulIntelligenceAgentResult<TResult>>
        ExecuteAsync(
            TObjective objective,
            IntelligenceExecutionContext context,
            string stateId,
            CancellationToken cancellationToken =
                default)
    {
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            stateId);

        if (!context.AgentId.HasValue ||
            context.AgentId.Value != _agent.Id)
        {
            throw new ArgumentException(
                "Execution context Agent ID must " +
                "match the stateful agent.",
                nameof(context));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await _auditWriter.WriteAsync<TResult>(
                _agent.Id,
                context,
                null,
                EMF.Security.Auditing.Models.SecurityAuditOutcome.Cancelled,
                DateTimeOffset.UtcNow);

            cancellationToken.ThrowIfCancellationRequested();
        }

        IntelligenceAgentState state;

        try
        {
            state =
                await _stateStore.GetAsync(
                    _agent.Id,
                    stateId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    $"State '{stateId}' was not found " +
                    $"for agent '{_agent.Id.Value}'.");

            IntelligenceAgentStateCompatibility
                .EnsureSupported(
                    _agent,
                    state);
        }
        catch (Exception)
        {
            await _auditWriter.WriteAsync<TResult>(
                _agent.Id,
                context,
                null,
                EMF.Security.Auditing.Models.SecurityAuditOutcome.Failed,
                DateTimeOffset.UtcNow);

            throw;
        }

        StatefulIntelligenceAgentResult<TResult> result;

        try
        {
            result =
                await _agent.ExecuteAsync(
                    objective,
                    context,
                    state,
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await _auditWriter.WriteAsync<TResult>(
                _agent.Id,
                context,
                null,
                EMF.Security.Auditing.Models.SecurityAuditOutcome.Cancelled,
                DateTimeOffset.UtcNow);

            throw;
        }
        catch (Exception)
        {
            await _auditWriter.WriteAsync<TResult>(
                _agent.Id,
                context,
                null,
                EMF.Security.Auditing.Models.SecurityAuditOutcome.Failed,
                DateTimeOffset.UtcNow);

            throw;
        }

        try
        {
            if (result.State.AgentId != _agent.Id ||
                result.State.StateId != stateId)
            {
                throw new InvalidOperationException(
                    "Stateful agent returned state for " +
                    "a different agent or state ID.");
            }

            IntelligenceAgentStateCompatibility
                .EnsureSupported(
                    _agent,
                    result.State);
        }
        catch (Exception)
        {
            await _auditWriter.WriteAsync(
                _agent.Id,
                context,
                result.Result,
                EMF.Security.Auditing.Models.SecurityAuditOutcome.Failed,
                DateTimeOffset.UtcNow);

            throw;
        }

        var stateToSave =
            new IntelligenceAgentState
            {
                AgentId = result.State.AgentId,
                StateId = result.State.StateId,
                Version = result.State.Version,
                Revision = state.Revision,
                Payload = result.State.Payload,
                UpdatedUtc = result.State.UpdatedUtc
            };

        try
        {
            await _stateStore.SaveAsync(
                stateToSave,
                cancellationToken);
        }
        catch
        {
            await _auditWriter.WriteAsync(
                _agent.Id,
                context,
                result.Result,
                EMF.Security.Auditing.Models.SecurityAuditOutcome.Failed,
                DateTimeOffset.UtcNow);

            throw;
        }

        await _auditWriter.WriteAsync(
            _agent.Id,
            context,
            result.Result,
            result.Result.Success
                ? EMF.Security.Auditing.Models.SecurityAuditOutcome.Succeeded
                : EMF.Security.Auditing.Models.SecurityAuditOutcome.Failed,
            DateTimeOffset.UtcNow);

        return result;
    }
}
