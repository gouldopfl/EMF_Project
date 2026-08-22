using EMF.Intelligence.Models;

namespace EMF.Intelligence.State;

public interface IStatefulIntelligenceAgent<
    TObjective,
    TResult> :
    IStatefulIntelligenceAgent
    where TObjective : notnull
    where TResult : notnull
{
    Task<StatefulIntelligenceAgentResult<TResult>>
        ExecuteAsync(
            TObjective objective,
            IntelligenceExecutionContext context,
            IntelligenceAgentState state,
            CancellationToken cancellationToken =
                default);
}
