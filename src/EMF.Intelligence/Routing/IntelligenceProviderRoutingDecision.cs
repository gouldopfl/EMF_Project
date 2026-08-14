namespace EMF.Intelligence.Routing;

public sealed class IntelligenceProviderRoutingDecision
{
    public required bool Permitted { get; init; }

    public string? Reason { get; init; }
}
