using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.State;

public interface IIntelligenceAgentStateStore
{
    Task<IntelligenceAgentState?> GetAsync(
        AgentId agentId,
        string stateId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        IntelligenceAgentState state,
        CancellationToken cancellationToken = default);
}
