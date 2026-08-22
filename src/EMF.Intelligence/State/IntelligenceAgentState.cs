using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.State;

public sealed class IntelligenceAgentState
{
    public required AgentId AgentId { get; init; }

    public required string StateId { get; init; }

    public required int Version { get; init; }

    public int Revision { get; init; }

    public required string Payload { get; init; }

    public required DateTimeOffset UpdatedUtc { get; init; }
}
