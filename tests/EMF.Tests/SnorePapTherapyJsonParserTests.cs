using System.Text;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class SnorePapTherapyJsonParserTests
{
    [Fact]
    public void Parse_ReadsSnoreAirCurveSession()
    {
        const string json = """
        {
          "snore_export_format": "1.0",
          "session_count": 1,
          "sessions": [{
            "device_session_id": "20250730_220208_merged",
            "date": "2025-07-30",
            "start_time": "2025-07-30T22:02:57",
            "duration_hours": 10.82,
            "device": {
              "model": "AirCurve11ASV"
            },
            "statistics": {
              "ahi": 9.592476489028213,
              "obstructive_apneas": 0,
              "central_apneas": 0,
              "mixed_apneas": 0,
              "unclassified_apneas": 44,
              "hypopneas": 58,
              "reras": 0,
              "pressure_mean": 12.42664681295716,
              "pressure_95th": 19.0,
              "leak_mean": 29.481128526645765,
              "leak_95th": 62.400001525878906,
              "usage_hours": 10.633333333333333
            }
          }]
        }
        """;

        var result =
            new SnorePapTherapyJsonParser()
                .Parse(Encoding.UTF8.GetBytes(json));

        var session = Assert.Single(result);

        Assert.Equal("20250730_220208_merged", session.SourceSessionId);
        Assert.Equal(new DateOnly(2025, 7, 30), session.Date);
        Assert.Equal(new TimeOnly(22, 2, 57), session.Start);
        Assert.Equal(9.592476489028213, session.SourceAhi, 12);
        Assert.Equal(44, session.UnclassifiedApneaCount);
        Assert.Equal(58, session.HypopneaCount);
        Assert.Equal(0, session.MixedApneaCount);
        Assert.Equal(10.633333333333333, session.HoursUsed, 12);
        Assert.Equal(19.0, session.Pressure95th);
        Assert.Equal(62.400001525878906, session.Leak95th);
        Assert.Equal("AirCurve11ASV", session.Machine);
    }

    [Fact]
    public void Parse_RejectsUnsupportedExportFormat()
    {
        var json = Encoding.UTF8.GetBytes(
            """{"snore_export_format":"2.0","session_count":0,"sessions":[]}""");

        Assert.Throws<InvalidDataException>(
            () => new SnorePapTherapyJsonParser().Parse(json));
    }

    [Fact]
    public void Parse_RejectsMismatchedSessionCount()
    {
        var json = Encoding.UTF8.GetBytes(
            """
            {
              "snore_export_format":"1.0",
              "session_count":2,
              "sessions":[{
                "device_session_id":"x",
                "date":"2025-07-30",
                "start_time":"2025-07-30T22:00:00",
                "duration_hours":1,
                "device":{"model":"AirCurve11ASV"},
                "statistics":{"ahi":1,"usage_hours":1}
              }]
            }
            """);

        Assert.Throws<InvalidDataException>(
            () => new SnorePapTherapyJsonParser().Parse(json));
    }

}
