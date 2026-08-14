using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Agents;

public sealed class IntelligenceAgentRegistry<
    TObjective,
    TResult>
    where TObjective : notnull
    where TResult : notnull
{
    private readonly IReadOnlyDictionary<
        AgentId,
        IIntelligenceAgent<
            TObjective,
            TResult>> _agents;

    public IntelligenceAgentRegistry(
        IEnumerable<
            IIntelligenceAgent<
                TObjective,
                TResult>> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        var configuredAgents = agents.ToArray();

        if (configuredAgents.Any(
                agent => agent is null))
        {
            throw new ArgumentException(
                "Configured intelligence agents cannot contain null.",
                nameof(agents));
        }

        if (configuredAgents.Any(
                agent =>
                    string.IsNullOrWhiteSpace(
                        agent.Id.Value)))
        {
            throw new ArgumentException(
                "Configured intelligence agents must have IDs.",
                nameof(agents));
        }

        if (configuredAgents
            .GroupBy(agent => agent.Id)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Configured intelligence agent IDs must be unique.",
                nameof(agents));
        }

        _agents =
            configuredAgents.ToDictionary(
                agent => agent.Id);
    }

    public IIntelligenceAgent<
        TObjective,
        TResult> Resolve(
            AgentId agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            agentId.Value);

        if (_agents.TryGetValue(
                agentId,
                out var agent))
        {
            return agent;
        }

        throw new
            IntelligenceAgentUnavailableException(
                agentId);
    }
}
