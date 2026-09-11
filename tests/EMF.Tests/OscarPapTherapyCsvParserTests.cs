using System.Text;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class OscarPapTherapyCsvParserTests
{
    [Fact]
    public void Parse_ReadsOscarBySessionExport()
    {
        const string csv =
            "Date,Start,AHI,RDI,OA,UA,H,CA,RERA," +
            "Pressure_Avg,Pressure_Min,Pressure_Max,Pressure_95th," +
            "Leak_Avg,Leak_Max,Leak_95th,SpO2_Avg,SpO2_Min," +
            "Pulse_Avg,Hours,Hours_Used,Machine\n" +
            "2025-07-30,22:02:12,0.31,0,0,0,1,0,0," +
            "8.89,6,15.84,13.18,0,0,0,,,," +
            "0.7,0.7,AirCurve11ASV\n";

        var result =
            new OscarPapTherapyCsvParser()
                .Parse(Encoding.UTF8.GetBytes(csv));

        var session = Assert.Single(result);

        Assert.Equal(
            new DateOnly(2025, 7, 30),
            session.Date);
        Assert.Equal(
            new TimeOnly(22, 2, 12),
            session.Start);

        Assert.Equal(0.31, session.SourceAhi);
        Assert.Equal(0.7, session.HoursUsed);
        Assert.Equal(13.18, session.Pressure95th);
        Assert.Equal("AirCurve11ASV", session.Machine);

        Assert.Null(session.AverageSpO2);
        Assert.Null(session.MinimumSpO2);
        Assert.Null(session.AveragePulse);
    }

    [Fact]
    public void Parse_RejectsMissingRequiredHeader()
    {
        var csv = Encoding.UTF8.GetBytes(
            "Date,Start,AHI\n2025-07-30,22:02:12,0.31\n");

        Assert.Throws<InvalidDataException>(
            () => new OscarPapTherapyCsvParser().Parse(csv));
    }

    [Fact]
    public void Parse_RejectsWrongColumnCount()
    {
        const string header =
            "Date,Start,AHI,RDI,OA,UA,H,CA,RERA," +
            "Pressure_Avg,Pressure_Min,Pressure_Max,Pressure_95th," +
            "Leak_Avg,Leak_Max,Leak_95th,SpO2_Avg,SpO2_Min," +
            "Pulse_Avg,Hours,Hours_Used,Machine\n";

        var csv = Encoding.UTF8.GetBytes(
            header + "2025-07-30,22:02:12,0.31\n");

        Assert.Throws<InvalidDataException>(
            () => new OscarPapTherapyCsvParser().Parse(csv));
    }

}
