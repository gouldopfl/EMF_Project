using EMF.Intelligence.Models;

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

    public StatefulIntelligenceAgentExecutor(
        IStatefulIntelligenceAgent<
            TObjective,
            TResult> agent,
        IIntelligenceAgentStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(stateStore);

        _agent = agent;
        _stateStore = stateStore;
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

        var state =
            await _stateStore.GetAsync(
                _agent.Id,
                stateId,
                cancellationToken);

        if (state is null)
        {
            throw new InvalidOperationException(
                $"State '{stateId}' was not found " +
                $"for agent '{_agent.Id.Value}'.");
        }

        IntelligenceAgentStateCompatibility
            .EnsureSupported(
                _agent,
                state);

        var result =
            await _agent.ExecuteAsync(
                objective,
                context,
                state,
                cancellationToken);

        await _stateStore.SaveAsync(
            result.State,
            cancellationToken);

        return result;
    }
}
