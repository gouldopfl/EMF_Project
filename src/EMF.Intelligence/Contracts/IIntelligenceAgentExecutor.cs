using EMF.Intelligence.Agents;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Contracts;

public interface IIntelligenceAgentExecutor<
    TObjective,
    TResult>
    where TObjective : notnull
    where TResult : notnull
{
    Task<IntelligenceAgentResult<TResult>>
        ExecuteAsync(
            AgentId agentId,
            TObjective objective,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default);
}
