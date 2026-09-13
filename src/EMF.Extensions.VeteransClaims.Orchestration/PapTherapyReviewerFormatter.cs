using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal static class PapTherapyReviewerFormatter
{
    public static string Format(ReadOnlyMemory<byte> csv)
    {
        var sessions =
            new OscarPapTherapyCsvParser().Parse(csv.Span);

        return FormatAnalysis(
            new PapTherapyAnalysisService().Analyze(sessions),
            "OSCAR");
    }

    public static string FormatSnore(ReadOnlyMemory<byte> json)
    {
        var sessions =
            new SnorePapTherapyJsonParser().Parse(json.Span);

        return FormatAnalysis(
            new PapTherapyAnalysisService().Analyze(sessions),
            "SNORE");
    }

    private static string FormatAnalysis(
        PapTherapyAnalysis a,
        string sourceName)
    {
        return $"""
PAP Therapy Analysis

Coverage: {a.StartDate:MM/dd/yyyy} through {a.EndDate:MM/dd/yyyy}
Sessions: {a.SessionCount}
Treatment days: {a.TreatmentDayCount}
Total therapy hours: {a.TotalHoursUsed:F2}
Average hours per treatment day: {a.AverageHoursPerTreatmentDay:F2}
Days >= 4 hours: {a.DaysAtLeastFourHours}
Days >= 6 hours: {a.DaysAtLeastSixHours}
Additional same-day sessions: {a.AdditionalSessionCount}

Weighted AHI: {a.WeightedAhi:F2}
Median daily AHI: {a.MedianDailyAhi:F2}
Maximum daily AHI: {a.MaximumDailyAhi:F2}

Machine(s): {string.Join(", ", a.Machines)}

Derived deterministically from the retained {sourceName} session export.
Raw {sourceName} session records are intentionally omitted from this physician report.
""";
    }
}
