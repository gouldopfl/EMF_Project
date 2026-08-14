using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Agents;

public sealed class
    IntelligenceAgentUnavailableException :
    InvalidOperationException
{
    public IntelligenceAgentUnavailableException(
        AgentId agentId)
        : base(
            "No configured intelligence agent is " +
            $"available for '{agentId.Value}'.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            agentId.Value);

        AgentId = agentId;
    }

    public AgentId AgentId { get; }
}
