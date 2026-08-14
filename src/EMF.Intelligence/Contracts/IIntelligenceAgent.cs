using EMF.Intelligence.Agents;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Contracts;

public interface IIntelligenceAgent<
    TObjective,
    TResult>
    where TObjective : notnull
    where TResult : notnull
{
    AgentId Id { get; }

    Task<IntelligenceAgentResult<TResult>>
        ExecuteAsync(
            TObjective objective,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default);
}
