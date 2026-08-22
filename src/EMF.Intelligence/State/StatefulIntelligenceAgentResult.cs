using EMF.Intelligence.Agents;

namespace EMF.Intelligence.State;

public sealed class StatefulIntelligenceAgentResult<TResult>
    where TResult : notnull
{
    public required IntelligenceAgentResult<TResult>
        Result { get; init; }

    public required IntelligenceAgentState State
    { get; init; }
}
