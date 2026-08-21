namespace EMF.Intelligence.State;

public static class IntelligenceAgentStateCompatibility
{
    public static void EnsureSupported(
        IStatefulIntelligenceAgent agent,
        IntelligenceAgentState state)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(state);

        if (state.Version > agent.SupportedStateVersion)
        {
            throw new InvalidOperationException(
                $"Agent state version {state.Version} is newer than " +
                $"supported version {agent.SupportedStateVersion}.");
        }
    }
}
