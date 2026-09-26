using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

/// <summary>
/// V1 freezes the complete input to the existing renderer, including selected printable
/// renditions. It is not a source evidence store or a promise of byte-identical DOCX/PDF.
/// Changes to this wire contract require a new version; historical rows are never upgraded.
/// </summary>
public sealed class VeteransReviewerPackageSnapshot
{
    public required int Version { get; init; }
    public required VeteransReviewerPackageDetails Details { get; init; }
    public required IReadOnlyList<VeteransReviewerApplicableRegulation> Regulations { get; init; }

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static ReviewerPackageSnapshot Capture(VeteransReviewerPackageDetails details,
        IReadOnlyList<VeteransReviewerApplicableRegulation> regulations)
    {
        var input = new VeteransReviewerPackageSnapshot { Version = 1, Details = details, Regulations = regulations };
        Validate(input, details.PackageDetails.Package.Id);
        var payload = Canonical(JsonSerializer.SerializeToElement(input, Options));
        var row = new ReviewerPackageSnapshot(details.PackageDetails.Package.Id, 1, payload,
            ReviewerPackageSnapshot.ComputeHash(payload));
        // Verify that exactly the captured contract can be restored before sealing it.
        Restore(row);
        return row;
    }

    public static VeteransReviewerPackageSnapshot Restore(ReviewerPackageSnapshot row)
    {
        row.ValidateIntegrity();
        try
        {
            using var document = JsonDocument.Parse(row.Payload);
            if (Canonical(document.RootElement) != row.Payload)
                throw new InvalidDataException("Reviewer snapshot is not canonical JSON.");
            var result = JsonSerializer.Deserialize<VeteransReviewerPackageSnapshot>(row.Payload, Options)
                ?? throw new InvalidDataException("Reviewer snapshot is empty.");
            Validate(result, row.PackageId);
            if (Canonical(JsonSerializer.SerializeToElement(result, Options)) != row.Payload)
                throw new InvalidDataException("Reviewer snapshot contract is incomplete or inconsistent.");
            return result;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException("Reviewer snapshot contract is invalid.", ex);
        }
    }

    private static void Validate(VeteransReviewerPackageSnapshot snapshot, EvidencePackageId id)
    {
        if (snapshot.Version != 1 || snapshot.Details?.PackageDetails?.Package?.Id != id || snapshot.Regulations is null)
            throw new InvalidDataException("Reviewer snapshot identity or version mismatch.");
        var d = snapshot.Details;
        var members = d.PackageDetails.Artifacts;
        if (members is null || d.Artifacts is null || d.ArtifactContents is null ||
            members.Any(x => x is null || x.EvidencePackageId != id || string.IsNullOrWhiteSpace(x.ArtifactId.Value) ||
                x.ContentRole is not (EvidencePackageContentRoles.UnderlyingEvidence or EvidencePackageContentRoles.GeneratedOrganizationalMaterial)) ||
            members.Select(x => x.ArtifactId).Distinct().Count() != members.Count ||
            !members.Select(x => x.ArtifactId).OrderBy(x => x.Value, StringComparer.Ordinal).SequenceEqual(
                d.Artifacts.Select(x => x.Id).OrderBy(x => x.Value, StringComparer.Ordinal)) ||
            !members.Select(x => x.ArtifactId).OrderBy(x => x.Value, StringComparer.Ordinal).SequenceEqual(
                d.ArtifactContents.Select(x => x.Artifact.Id).OrderBy(x => x.Value, StringComparer.Ordinal)))
            throw new InvalidDataException("Reviewer snapshot member coverage is invalid.");
        foreach (var member in members)
        {
            var content = d.ArtifactContents.Single(x => x.Artifact.Id == member.ArtifactId);
            if ((member.ContentRole != EvidencePackageContentRoles.UnderlyingEvidence && member.ReviewerPageSelection is not null) ||
                (content.Appendix == VeteransReviewerPackageAppendix.MedicalLiterature && content.ReviewedMedicalLiteratureClassifications.Count == 0) ||
                content.ReviewerPageSelection != member.ReviewerPageSelection ||
                (member.ContentRole == EvidencePackageContentRoles.UnderlyingEvidence && content.PrintablePages.Count == 0) ||
                content.PrintablePages.Any(p => p.PageNumber < 1 || p.Content.IsEmpty) ||
                content.PrintablePages.Select(p => p.PageNumber).Distinct().Count() != content.PrintablePages.Count)
                throw new InvalidDataException("Reviewer snapshot printable pages or selections are incomplete.");
            if (member.ReviewerPageSelection is not null &&
                !VeteransReviewerPageSelector.Select(content.PrintablePages, member.ReviewerPageSelection)
                    .Select(p => p.PageNumber).SequenceEqual(content.PrintablePages.Select(p => p.PageNumber)))
                throw new InvalidDataException("Reviewer snapshot selected pages are inconsistent.");
        }
        var expected = (d.MedicalOpinionRequested?.ApplicableRegulatoryCitations ?? [])
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        if (snapshot.Regulations.Any(x => string.IsNullOrWhiteSpace(x.Text) || string.IsNullOrWhiteSpace(x.SourceSha256) ||
                string.IsNullOrWhiteSpace(x.SourceUri)) ||
            !expected.SequenceEqual(snapshot.Regulations.Select(x => x.Citation.Trim()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Reviewer snapshot regulatory coverage is invalid.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind != JsonTypeInfoKind.Object) return;
            if (!VeteransReviewerSnapshotV1Contract.Fields.TryGetValue(info.Type, out var fields))
                throw new NotSupportedException("Type is not part of the reviewer snapshot V1 contract.");
            info.Properties.Clear();
            foreach (var field in fields)
            {
                var reflection = info.Type.GetProperty(field)
                    ?? throw new NotSupportedException("Reviewer snapshot V1 field is unavailable.");
                var property = info.CreateJsonPropertyInfo(reflection.PropertyType, field);
                property.Get = reflection.GetValue;
                property.Set = reflection.SetValue;
                property.IsRequired = true;
                var nullable = VeteransReviewerSnapshotV1Contract.NullableFields.Contains((info.Type, field));
                property.IsSetNullable = nullable;
                property.IsGetNullable = nullable;
                info.Properties.Add(property);
            }
        });
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = resolver,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true
        };
        options.Converters.Add(new IdentityConverterFactory());
        options.Converters.Add(new InvariantDateTimeOffsetConverter());
        return options;
    }

    internal static string Canonical(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            var properties = value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
            if (properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                throw new InvalidDataException("Duplicate reviewer snapshot JSON property.");
            foreach (var property in properties) { writer.WritePropertyName(property.Name); Write(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Null) throw new InvalidDataException("Null reviewer snapshot collection item.");
                Write(writer, item);
            }
            writer.WriteEndArray();
        }
        else if (value.ValueKind == JsonValueKind.Number)
            writer.WriteRawValue(CanonicalNumber(value.GetRawText()));
        else value.WriteTo(writer);
    }

    // Normalize the exact decimal token, not a rounded decimal/double conversion.
    // This also preserves high-precision numbers in arbitrary artifact metadata.
    private static string CanonicalNumber(string raw)
    {
        var negative = raw.StartsWith('-');
        var token = negative ? raw[1..] : raw;
        var exponentIndex = token.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (exponentIndex >= 0)
        {
            if (!int.TryParse(token[(exponentIndex + 1)..], System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out exponent) || exponent is < -4096 or > 4096)
                throw new InvalidDataException("Reviewer snapshot numeric exponent exceeds V1 limits.");
            token = token[..exponentIndex];
        }
        var point = token.IndexOf('.');
        var scale = point < 0 ? 0 : token.Length - point - 1;
        var digits = token.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return "0";
        if (digits.Length > 4096) throw new InvalidDataException("Reviewer snapshot number exceeds V1 limits.");
        var significant = digits.TrimEnd('0');
        var position = digits.Length + exponent - scale;
        if (position is < -4096 or > 4096)
            throw new InvalidDataException("Reviewer snapshot numeric expansion exceeds V1 limits.");
        var normalized = position <= 0 ? "0." + new string('0', -position) + significant :
            position >= significant.Length ? significant + new string('0', position - significant.Length) :
            significant.Insert(position, ".");
        return negative ? "-" + normalized : normalized;
    }

    private sealed class InvariantDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDateTimeOffset();
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class IdentityConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type type) => type.IsValueType &&
            type.Namespace?.EndsWith(".Models.Identities", StringComparison.Ordinal) == true &&
            type.GetProperty("Value")?.PropertyType == typeof(string);
        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(IdentityConverter<>).MakeGenericType(type))!;
    }

    private sealed class IdentityConverter<T> : JsonConverter<T> where T : struct
    {
        private static string Value(T value) => (string)typeof(T).GetProperty("Value")!.GetValue(value)!;
        private static T Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new JsonException("Empty snapshot identity.");
            return (T)Activator.CreateInstance(typeof(T), value)!;
        }
        public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => Parse(reader.GetString());
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var text = Value(value);
            if (string.IsNullOrWhiteSpace(text)) throw new JsonException("Empty snapshot identity.");
            writer.WriteStringValue(text);
        }
        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => Parse(reader.GetString());
        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WritePropertyName(Value(value));
    }
}
