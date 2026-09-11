using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class PapTherapyAnalysisService
{
    public PapTherapyAnalysis Analyze(
        IReadOnlyList<PapTherapySession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (sessions.Count == 0)
            throw new ArgumentException(
                "At least one PAP therapy session is required.",
                nameof(sessions));

        foreach (var session in sessions)
            ValidateSession(session);

        var ordered = sessions
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Start)
            .ToArray();

        var daily = ordered
            .GroupBy(x => x.Date)
            .Select(group =>
            {
                var positive =
                    group.Where(x => x.HoursUsed > 0).ToArray();

                var hours = positive.Sum(x => x.HoursUsed);

                return new PapTherapyDailySummary
                {
                    Date = group.Key,
                    SessionCount = group.Count(),
                    HoursUsed = hours,
                    WeightedAhi = hours == 0
                        ? null
                        : positive.Sum(x => x.SourceAhi * x.HoursUsed) /
                          hours
                };
            })
            .OrderBy(x => x.Date)
            .ToArray();

        var therapyDays =
            daily
                .Where(x => x.HoursUsed > 0)
                .ToArray();

        if (therapyDays.Length == 0)
            throw new InvalidOperationException(
                "PAP analysis requires recorded therapy time.");

        var totalHours =
            therapyDays.Sum(x => x.HoursUsed);

        var dailyAhi =
            therapyDays
                .Select(x => x.WeightedAhi!.Value)
                .OrderBy(x => x)
                .ToArray();

        var pressure95 = ordered
            .Where(x => x.Pressure95th.HasValue)
            .Select(x => x.Pressure95th!.Value)
            .OrderBy(x => x)
            .ToArray();

        var startDate = daily[0].Date;
        var endDate = daily[^1].Date;

        return new PapTherapyAnalysis
        {
            StartDate = startDate,
            EndDate = endDate,
            CalendarDayCount =
                endDate.DayNumber - startDate.DayNumber + 1,
            SessionCount = ordered.Length,
            ZeroDurationSessionCount =
                ordered.Count(x => x.HoursUsed == 0),
            TreatmentDayCount = therapyDays.Length,
            TotalHoursUsed = totalHours,
            AverageHoursPerTreatmentDay =
                totalHours / therapyDays.Length,
            DaysAtLeastFourHours =
                therapyDays.Count(x => x.HoursUsed >= 4),
            DaysAtLeastSixHours =
                therapyDays.Count(x => x.HoursUsed >= 6),
            AdditionalSessionCount =
                ordered.Length - daily.Length,
            WeightedAhi =
                ordered.Sum(x => x.SourceAhi * x.HoursUsed) /
                totalHours,
            MedianDailyAhi = Median(dailyAhi),
            MaximumDailyAhi = dailyAhi[^1],
            AhiUnderFiveDays =
                therapyDays.Count(x => x.WeightedAhi!.Value < 5),
            AhiFiveToUnderTenDays =
                therapyDays.Count(x =>
                    x.WeightedAhi!.Value >= 5 &&
                    x.WeightedAhi.Value < 10),
            AhiTenToUnderFifteenDays =
                therapyDays.Count(x =>
                    x.WeightedAhi!.Value >= 10 &&
                    x.WeightedAhi.Value < 15),
            AhiAtLeastFifteenDays =
                therapyDays.Count(x => x.WeightedAhi!.Value >= 15),
            MedianPressure95th =
                pressure95.Length == 0
                    ? null
                    : Median(pressure95),
            MaximumPressure95th =
                pressure95.Length == 0
                    ? null
                    : pressure95[^1],
            HasNonZeroLeakMeasurements =
                ordered.Any(x =>
                    NonZero(x.AverageLeak) ||
                    NonZero(x.MaximumLeak) ||
                    NonZero(x.Leak95th)),
            SpO2MeasurementCount =
                ordered.Count(x =>
                    x.AverageSpO2.HasValue ||
                    x.MinimumSpO2.HasValue),
            Machines = ordered
                .Select(x => x.Machine.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DailySummaries = daily
        };
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var middle = values.Count / 2;

        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2;
    }

    private static bool NonZero(double? value) =>
        value.HasValue && value.Value != 0;

    private static void ValidateSession(PapTherapySession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!double.IsFinite(session.SourceAhi) || session.SourceAhi < 0)
            throw new ArgumentOutOfRangeException(nameof(session.SourceAhi));

        if (!double.IsFinite(session.HoursUsed) ||
            session.HoursUsed < 0)
            throw new ArgumentOutOfRangeException(
                nameof(session.HoursUsed));

        if (string.IsNullOrWhiteSpace(session.Machine))
            throw new ArgumentException(
                "PAP therapy machine is required.",
                nameof(session.Machine));
    }
}
