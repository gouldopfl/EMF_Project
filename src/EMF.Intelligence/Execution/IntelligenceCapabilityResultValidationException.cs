namespace EMF.Intelligence.Execution;

public sealed class
    IntelligenceCapabilityResultValidationException :
    InvalidOperationException
{
    public IntelligenceCapabilityResultValidationException(
        string reason)
        : base(reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            reason);
    }
}
