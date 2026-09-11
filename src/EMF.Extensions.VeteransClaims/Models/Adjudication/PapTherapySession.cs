namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class PapTherapySession
{
    public required DateOnly Date { get; init; }

    public TimeOnly? Start { get; init; }

    public required double SourceAhi { get; init; }

    public string? SourceSessionId { get; init; }

    public double? Rdi { get; init; }

    public double? ObstructiveApneaCount { get; init; }

    public double? UnclassifiedApneaCount { get; init; }

    public double? MixedApneaCount { get; init; }

    public double? HypopneaCount { get; init; }

    public double? CentralApneaCount { get; init; }

    public double? ReraCount { get; init; }

    public double? AveragePressure { get; init; }

    public double? MinimumPressure { get; init; }

    public double? MaximumPressure { get; init; }

    public double? Pressure95th { get; init; }

    public double? AverageLeak { get; init; }

    public double? MaximumLeak { get; init; }

    public double? Leak95th { get; init; }

    public double? AverageSpO2 { get; init; }

    public double? MinimumSpO2 { get; init; }

    public double? AveragePulse { get; init; }

    public double? SessionHours { get; init; }

    public required double HoursUsed { get; init; }

    public required string Machine { get; init; }
}
