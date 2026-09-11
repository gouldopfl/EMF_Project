namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class PapTherapyAnalysis
{
    public required DateOnly StartDate { get; init; }

    public required DateOnly EndDate { get; init; }

    public required int CalendarDayCount { get; init; }

    public required int SessionCount { get; init; }

    public required int ZeroDurationSessionCount { get; init; }

    public required int TreatmentDayCount { get; init; }

    public required double TotalHoursUsed { get; init; }

    public required double AverageHoursPerTreatmentDay { get; init; }

    public required int DaysAtLeastFourHours { get; init; }

    public required int DaysAtLeastSixHours { get; init; }

    public required int AdditionalSessionCount { get; init; }

    public required double WeightedAhi { get; init; }

    public required double MedianDailyAhi { get; init; }

    public required double MaximumDailyAhi { get; init; }

    public required int AhiUnderFiveDays { get; init; }

    public required int AhiFiveToUnderTenDays { get; init; }

    public required int AhiTenToUnderFifteenDays { get; init; }

    public required int AhiAtLeastFifteenDays { get; init; }

    public double? MedianPressure95th { get; init; }

    public double? MaximumPressure95th { get; init; }

    public required bool HasNonZeroLeakMeasurements { get; init; }

    public required int SpO2MeasurementCount { get; init; }

    public required IReadOnlyList<string> Machines { get; init; }

    public required IReadOnlyList<PapTherapyDailySummary>
        DailySummaries { get; init; }
}
