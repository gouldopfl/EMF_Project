using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class PapTherapyAnalysisServiceTests
{
    [Fact]
    public void Analyze_AggregatesPapTherapySessions()
    {
        PapTherapySession[] sessions =
        [
            Session("2026-01-01", 2, 2, 10),
            Session("2026-01-01", 6, 6, 14),
            Session("2026-01-02", 1, 4, 12),
            Session("2026-01-03", 12, 6, 18),
            Session("2026-01-04", 16, 7, 16)
        ];

        var result =
            new PapTherapyAnalysisService()
                .Analyze(sessions);

        Assert.Equal(4, result.CalendarDayCount);
        Assert.Equal(5, result.SessionCount);
        Assert.Equal(4, result.TreatmentDayCount);
        Assert.Equal(25, result.TotalHoursUsed, 6);
        Assert.Equal(6.25, result.AverageHoursPerTreatmentDay, 6);
        Assert.Equal(4, result.DaysAtLeastFourHours);
        Assert.Equal(3, result.DaysAtLeastSixHours);
        Assert.Equal(1, result.AdditionalSessionCount);

        Assert.Equal(9.12, result.WeightedAhi, 6);
        Assert.Equal(8.5, result.MedianDailyAhi, 6);
        Assert.Equal(16, result.MaximumDailyAhi, 6);

        Assert.Equal(1, result.AhiUnderFiveDays);
        Assert.Equal(1, result.AhiFiveToUnderTenDays);
        Assert.Equal(1, result.AhiTenToUnderFifteenDays);
        Assert.Equal(1, result.AhiAtLeastFifteenDays);

        Assert.Equal(14, result.MedianPressure95th);
        Assert.Equal(18, result.MaximumPressure95th);
        Assert.False(result.HasNonZeroLeakMeasurements);
        Assert.Equal(0, result.SpO2MeasurementCount);

        Assert.Equal(["AirCurve11ASV"], result.Machines);
        Assert.Equal(4, result.DailySummaries.Count);
        Assert.Equal(5, result.DailySummaries[0].WeightedAhi!.Value, 6);
    }

    [Fact]
    public void Analyze_PreservesZeroDurationSessionsAndCountsShortTherapy()
    {
        PapTherapySession[] sessions =
        [
            Session("2026-01-05", 0, 0, 10),
            Session("2026-01-06", 4, 0.5, 11)
        ];

        var result =
            new PapTherapyAnalysisService()
                .Analyze(sessions);

        Assert.Equal(2, result.CalendarDayCount);
        Assert.Equal(2, result.SessionCount);
        Assert.Equal(1, result.ZeroDurationSessionCount);
        Assert.Equal(1, result.TreatmentDayCount);
        Assert.Equal(0.5, result.TotalHoursUsed, 6);
        Assert.Equal(0.5, result.AverageHoursPerTreatmentDay, 6);
        Assert.Equal(0, result.DaysAtLeastFourHours);
        Assert.Equal(0, result.DaysAtLeastSixHours);

        Assert.Equal(2, result.DailySummaries.Count);
        Assert.Null(result.DailySummaries[0].WeightedAhi);
        Assert.Equal(
            4,
            result.DailySummaries[1].WeightedAhi!.Value,
            6);
    }

    private static PapTherapySession Session(
        string date,
        double ahi,
        double hours,
        double pressure95)
    {
        return new PapTherapySession
        {
            Date = DateOnly.Parse(date),
            SourceAhi = ahi,
            HoursUsed = hours,
            Pressure95th = pressure95,
            AverageLeak = 0,
            MaximumLeak = 0,
            Leak95th = 0,
            Machine = "AirCurve11ASV"
        };
    }
}
