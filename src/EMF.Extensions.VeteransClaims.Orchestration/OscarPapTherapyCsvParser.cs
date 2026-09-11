using System.Globalization;
using System.Text;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class OscarPapTherapyCsvParser
{
    private static readonly string[] RequiredHeaders =
    [
        "Date", "Start", "AHI", "RDI",
        "OA", "UA", "H", "CA", "RERA",
        "Pressure_Avg", "Pressure_Min",
        "Pressure_Max", "Pressure_95th",
        "Leak_Avg", "Leak_Max", "Leak_95th",
        "SpO2_Avg", "SpO2_Min", "Pulse_Avg",
        "Hours", "Hours_Used", "Machine"
    ];

    public IReadOnlyList<PapTherapySession> Parse(
        ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            throw new InvalidDataException(
                "OSCAR CSV is empty.");

        string text;

        try
        {
            text =
                new UTF8Encoding(false, true)
                    .GetString(content);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "OSCAR CSV is not valid UTF-8.",
                exception);
        }

        using var reader = new StringReader(text);

        var headerLine = reader.ReadLine();

        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidDataException(
                "OSCAR CSV header is missing.");

        var headers = ParseRow(
            headerLine.TrimStart('\uFEFF'));

        ValidateHeaders(headers);

        var indexes = headers
            .Select((name, index) => (name, index))
            .ToDictionary(
                x => x.name,
                x => x.index,
                StringComparer.OrdinalIgnoreCase);

        var sessions = new List<PapTherapySession>();
        var lineNumber = 1;

        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var values = ParseRow(line);

            if (values.Count != headers.Count)
                throw new InvalidDataException(
                    $"OSCAR CSV line {lineNumber} has " +
                    $"{values.Count} columns; expected {headers.Count}.");

            sessions.Add(
                new PapTherapySession
                {
                    Date = DateOnly.ParseExact(
                        Value("Date"),
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),
                    Start = ParseTime(Value("Start")),
                    SourceAhi = RequiredDouble("AHI"),
                    Rdi = OptionalDouble("RDI"),
                    ObstructiveApneaCount = OptionalDouble("OA"),
                    UnclassifiedApneaCount = OptionalDouble("UA"),
                    HypopneaCount = OptionalDouble("H"),
                    CentralApneaCount = OptionalDouble("CA"),
                    ReraCount = OptionalDouble("RERA"),
                    AveragePressure = OptionalDouble("Pressure_Avg"),
                    MinimumPressure = OptionalDouble("Pressure_Min"),
                    MaximumPressure = OptionalDouble("Pressure_Max"),
                    Pressure95th = OptionalDouble("Pressure_95th"),
                    AverageLeak = OptionalDouble("Leak_Avg"),
                    MaximumLeak = OptionalDouble("Leak_Max"),
                    Leak95th = OptionalDouble("Leak_95th"),
                    AverageSpO2 = OptionalDouble("SpO2_Avg"),
                    MinimumSpO2 = OptionalDouble("SpO2_Min"),
                    AveragePulse = OptionalDouble("Pulse_Avg"),
                    SessionHours = OptionalDouble("Hours"),
                    HoursUsed = RequiredDouble("Hours_Used"),
                    Machine = Value("Machine")
                });

            string Value(string name) =>
                values[indexes[name]].Trim();

            double RequiredDouble(string name)
            {
                var value = Value(name);

                if (!double.TryParse(
                        value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var result))
                {
                    throw new InvalidDataException(
                        $"OSCAR CSV line {lineNumber} has " +
                        $"invalid {name} value '{value}'.");
                }

                return result;
            }

            double? OptionalDouble(string name)
            {
                var value = Value(name);

                if (value.Length == 0)
                    return null;

                return RequiredDouble(name);
            }
        }

        if (sessions.Count == 0)
            throw new InvalidDataException(
                "OSCAR CSV contains no therapy sessions.");

        return sessions;
    }

    private static void ValidateHeaders(
        IReadOnlyList<string> headers)
    {
        foreach (var required in RequiredHeaders)
        {
            if (!headers.Contains(
                    required,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"OSCAR CSV is missing required column '{required}'.");
            }
        }
    }

    private static TimeOnly? ParseTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return TimeOnly.ParseExact(
            value,
            "HH:mm:ss",
            CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> ParseRow(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (quoted && i + 1 < line.Length &&
                    line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (c == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        if (quoted)
            throw new InvalidDataException(
                "OSCAR CSV contains an unterminated quoted field.");

        values.Add(current.ToString());

        return values;
    }
}
