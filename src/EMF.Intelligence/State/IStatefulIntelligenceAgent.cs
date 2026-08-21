using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.State;

public interface IStatefulIntelligenceAgent
{
    AgentId Id { get; }

    int SupportedStateVersion { get; }
}
