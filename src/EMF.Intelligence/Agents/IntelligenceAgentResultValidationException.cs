namespace EMF.Intelligence.Agents;

public sealed class
    IntelligenceAgentResultValidationException :
    InvalidOperationException
{
    public IntelligenceAgentResultValidationException(
        string reason)
        : base(reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            reason);
    }
}
