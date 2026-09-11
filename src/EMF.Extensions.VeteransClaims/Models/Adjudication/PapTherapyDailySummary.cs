namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class PapTherapyDailySummary
{
    public required DateOnly Date { get; init; }

    public required int SessionCount { get; init; }

    public required double HoursUsed { get; init; }

    public required double? WeightedAhi { get; init; }
}
