using System.Globalization;
using System.Text.Json;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class SnorePapTherapyJsonParser
{
    public IReadOnlyList<PapTherapySession> Parse(
        ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            throw new InvalidDataException("SNORE JSON is empty.");

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(content.ToArray());
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "SNORE JSON is invalid.",
                exception);
        }

        using (document)
        {
            var root = document.RootElement;

            var format =
                RequiredString(root, "snore_export_format");

            if (!string.Equals(
                    format,
                    "1.0",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Unsupported SNORE export format '{format}'.");
            }

            if (!root.TryGetProperty("sessions", out var sessions) ||
                sessions.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "SNORE JSON sessions array is missing.");
            }

            var result = new List<PapTherapySession>();

            foreach (var item in sessions.EnumerateArray())
                result.Add(ParseSession(item));

            if (result.Count == 0)
                throw new InvalidDataException(
                    "SNORE JSON contains no therapy sessions.");

            if (!root.TryGetProperty("session_count", out var count) ||
                !count.TryGetInt32(out var expected) ||
                expected != result.Count)
            {
                throw new InvalidDataException(
                    "SNORE JSON session_count does not match sessions.");
            }

            return result;
        }
    }

    private static PapTherapySession ParseSession(JsonElement item)
    {
        var statistics = RequiredObject(item, "statistics");
        var device = RequiredObject(item, "device");

        var dateText = RequiredString(item, "date");
        if (!DateOnly.TryParseExact(
                dateText, "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
            throw new InvalidDataException(
                $"SNORE session has invalid date '{dateText}'.");

        var startText = RequiredString(item, "start_time");
        if (!DateTime.TryParse(
                startText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var start))
            throw new InvalidDataException(
                $"SNORE session has invalid start_time '{startText}'.");

        return new PapTherapySession
        {
            Date = date,
            Start = TimeOnly.FromDateTime(start),
            SourceSessionId = RequiredString(item, "device_session_id"),
            SourceAhi = RequiredNumber(statistics, "ahi"),
            ObstructiveApneaCount =
                OptionalNumber(statistics, "obstructive_apneas"),
            CentralApneaCount =
                OptionalNumber(statistics, "central_apneas"),
            MixedApneaCount =
                OptionalNumber(statistics, "mixed_apneas"),
            UnclassifiedApneaCount =
                OptionalNumber(statistics, "unclassified_apneas"),
            HypopneaCount =
                OptionalNumber(statistics, "hypopneas"),
            ReraCount = OptionalNumber(statistics, "reras"),
            AveragePressure =
                OptionalNumber(statistics, "pressure_mean"),
            Pressure95th =
                OptionalNumber(statistics, "pressure_95th"),
            AverageLeak =
                OptionalNumber(statistics, "leak_mean"),
            Leak95th =
                OptionalNumber(statistics, "leak_95th"),
            SessionHours = RequiredNumber(item, "duration_hours"),
            HoursUsed = RequiredNumber(statistics, "usage_hours"),
            Machine = RequiredString(device, "model")
        };
    }

    private static JsonElement RequiredObject(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                $"SNORE JSON object '{name}' is missing.");

        return value;
    }

    private static double RequiredNumber(
        JsonElement parent,
        string name)
    {
        var value = OptionalNumber(parent, name);

        if (!value.HasValue)
            throw new InvalidDataException(
                $"SNORE JSON value '{name}' is missing.");

        return value.Value;
    }

    private static double? OptionalNumber(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result) ||
            result < 0)
            throw new InvalidDataException(
                $"SNORE JSON value '{name}' is invalid.");

        return result;
    }

    private static string RequiredString(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException(
                $"SNORE JSON value '{name}' is missing.");
        }

        return value.GetString()!;
    }
}
