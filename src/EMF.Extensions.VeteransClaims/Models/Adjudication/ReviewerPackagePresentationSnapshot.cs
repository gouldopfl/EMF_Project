using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

/// <summary>Resolved cover values. Missing values stay missing; prose is not an authority.</summary>
public sealed record ReviewerPackageCover(
    string? Veteran, string? ClaimType, string? ClaimedCondition, string? Basis,
    string? PreparedBy, string ReviewerRole);

/// <summary>Pixel rectangle in the oriented, privacy-masked source-page raster.</summary>
public sealed record ReviewerFigureTableEnlargement(
    string SourceArtifactId, int SourcePage, int SourceWidth, int SourceHeight,
    int ClockwiseRotation, int Left, int Top, int Width, int Height,
    string CoordinateSystem, string Orientation, string State, string? RejectionReason,
    string Kind, string DetectionVersion, string DetectionMethod, string Confidence,
    double EnlargementScale, long DisplayWidthEmus, long DisplayHeightEmus,
    string SourceImageSha256, string MaskingRotationVersion, string RendererBuild, string? CropImageSha256)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceArtifactId) || SourcePage <= 0 ||
            SourceWidth <= 0 || SourceHeight <= 0 || ClockwiseRotation is not (0 or 90 or 180 or 270) ||
            Left < 0 || Top < 0 || Width <= 0 || Height <= 0 ||
            (long)Left + Width > SourceWidth || (long)Top + Height > SourceHeight ||
            CoordinateSystem != "oriented-raster-pixels/top-left/half-open" ||
            Orientation != (SourceWidth > SourceHeight ? "Landscape" : "Portrait") ||
            State is not ("Detected" or "Selected" or "Frozen") ||
            Kind is not ("Figure" or "Table" or "Unknown") || DetectionVersion is not ("caption-grid-v1" or "caption-grid-v2") ||
            DetectionMethod is not ("ruled-grid" or "caption-and-isolated-raster" or "isolated-raster") ||
            Confidence is not ("High" or "Low") ||
            State == "Detected" && string.IsNullOrWhiteSpace(RejectionReason) ||
            State != "Detected" && (RejectionReason is not null || Confidence != "High" || Kind == "Unknown") ||
            !double.IsFinite(EnlargementScale) || EnlargementScale <= 0 ||
            State != "Detected" && EnlargementScale < 1.35 ||
            DisplayWidthEmus <= 0 || DisplayHeightEmus <= 0 ||
            Math.Abs(DisplayWidthEmus / (double)Width - DisplayHeightEmus / (double)Height) > 1 ||
            MaskingRotationVersion != "page-privacy-v1/source-orientation-v1" || string.IsNullOrWhiteSpace(RendererBuild) ||
            !ReviewerPackagePresentationSnapshot.IsHash(SourceImageSha256) ||
            State != "Detected" && !ReviewerPackagePresentationSnapshot.IsHash(CropImageSha256) ||
            State == "Detected" && CropImageSha256 is not null)
            throw new InvalidDataException("Invalid frozen figure/table enlargement geometry.");
    }
}

/// <summary>Immutable value collection, preserving snapshot equality across JSON round trips.</summary>
[JsonConverter(typeof(ReviewerFigureTableRegionsJsonConverter))]
public sealed class ReviewerFigureTableRegions : IReadOnlyList<ReviewerFigureTableEnlargement>,
    IEquatable<ReviewerFigureTableRegions>
{
    private readonly ReviewerFigureTableEnlargement[] _regions;
    public ReviewerFigureTableRegions(IEnumerable<ReviewerFigureTableEnlargement> regions) => _regions = regions.ToArray();
    public int Count => _regions.Length;
    public ReviewerFigureTableEnlargement this[int index] => _regions[index];
    public IEnumerator<ReviewerFigureTableEnlargement> GetEnumerator() =>
        ((IEnumerable<ReviewerFigureTableEnlargement>)_regions).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(ReviewerFigureTableRegions? other) => other is not null && _regions.SequenceEqual(other._regions);
    public override bool Equals(object? other) => other is ReviewerFigureTableRegions regions && Equals(regions);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var region in _regions) hash.Add(region);
        return hash.ToHashCode();
    }
}

internal sealed class ReviewerFigureTableRegionsJsonConverter : JsonConverter<ReviewerFigureTableRegions>
{
    public override ReviewerFigureTableRegions Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        new(JsonSerializer.Deserialize<ReviewerFigureTableEnlargement[]>(ref reader, options)
            ?? throw new JsonException("Missing figure/table region collection."));
    public override void Write(Utf8JsonWriter writer, ReviewerFigureTableRegions value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.ToArray(), options);
}

/// <summary>
/// Immutable compiled presentation, linked to the unchanged V1 evidence snapshot.
/// Source/clinical resolution happens before this boundary. Reprint materializes
/// this plan rather than interpreting evidence again. See the determinism contract.
/// </summary>
public sealed record ReviewerPackagePresentationSnapshot(
    [property: JsonConverter(typeof(ReviewerPresentationPackageIdJsonConverter))]
    EvidencePackageId PackageId, int Version, string SourceSnapshotSha256,
    DateOnly PackagePreparedDate, ReviewerPackageCover Cover,
    string RenderProfile, string PreparationRendererBuild,
    string DocxBase64, string DocxSha256, string Sha256)
{
    public string? PreviousPackageId { get; init; }

    // Omit absent plans so historical V1 hashes remain valid. New preparations
    // explicitly freeze an empty or populated list alongside the compiled DOCX.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReviewerFigureTableRegions? FigureTableRegions { get; init; }

    public static ReviewerPackagePresentationSnapshot Create(
        ReviewerPackageSnapshot source, DateOnly date, ReviewerPackageCover cover,
        string profile, string rendererBuild, ReadOnlyMemory<byte> docx, string? previousPackageId = null,
        IReadOnlyList<ReviewerFigureTableEnlargement>? figureTableRegions = null)
    {
        source.ValidateIntegrity();
        var result = new ReviewerPackagePresentationSnapshot(source.PackageId, 1,
            source.Sha256, date, cover, profile, rendererBuild,
            Convert.ToBase64String(docx.Span), Hash(docx.Span), string.Empty)
        { PreviousPackageId = previousPackageId, FigureTableRegions = figureTableRegions is null ? null : new(figureTableRegions) };
        result = result with { Sha256 = result.ComputeHash() };
        result.ValidateIntegrity();
        return result;
    }

    public byte[] MaterializeDocx()
    {
        ValidateIntegrity();
        return Convert.FromBase64String(DocxBase64);
    }

    public void ValidateIntegrity()
    {
        if (FigureTableRegions is not null)
            foreach (var region in FigureTableRegions)
            {
                (region ?? throw new InvalidDataException("Missing frozen enlargement geometry.")).Validate();
                if (region.State == "Selected" || region.RendererBuild != PreparationRendererBuild)
                    throw new InvalidDataException("Enlargement geometry is not frozen under this presentation renderer.");
            }
        if (Version != 1 || string.IsNullOrWhiteSpace(PackageId.Value) ||
            PackagePreparedDate == default || PreviousPackageId == PackageId.Value ||
            PreviousPackageId is not null && string.IsNullOrWhiteSpace(PreviousPackageId) || Cover is null ||
            string.IsNullOrWhiteSpace(Cover.ReviewerRole) ||
            string.IsNullOrWhiteSpace(RenderProfile) || string.IsNullOrWhiteSpace(PreparationRendererBuild) ||
            !IsHash(SourceSnapshotSha256) || !IsHash(DocxSha256) || !IsHash(Sha256) ||
            !string.Equals(Sha256, ComputeHash(), StringComparison.Ordinal))
            throw new InvalidDataException("Prepared reviewer presentation integrity failure.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(DocxBase64); }
        catch (FormatException ex) { throw new InvalidDataException("Invalid prepared DOCX encoding.", ex); }
        if (bytes.Length < 4 || bytes[0] != 'P' || bytes[1] != 'K' ||
            !string.Equals(DocxSha256, Hash(bytes), StringComparison.Ordinal))
            throw new InvalidDataException("Prepared DOCX integrity failure.");
    }

    private string ComputeHash() => Hash(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(this with { Sha256 = string.Empty })));
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}

/// <summary>Frozen PDF presentation. Conversion is preparation, never a preserved reprint.</summary>
public sealed record ReviewerPackageFrozenPdf(
    [property: JsonConverter(typeof(ReviewerPresentationPackageIdJsonConverter))]
    EvidencePackageId PackageId, int Version, string PresentationSha256,
    string ConverterIdentity, string ConverterVersion, string PdfBase64,
    string PdfSha256, string Sha256)
{
    public static ReviewerPackageFrozenPdf Create(ReviewerPackagePresentationSnapshot presentation,
        string identity, string version, ReadOnlyMemory<byte> pdf)
    {
        presentation.ValidateIntegrity();
        var result = new ReviewerPackageFrozenPdf(presentation.PackageId, 1, presentation.Sha256,
            identity, version, Convert.ToBase64String(pdf.Span),
            ReviewerPackagePresentationSnapshot.Hash(pdf.Span), string.Empty);
        result = result with { Sha256 = result.ComputeHash() };
        result.ValidateIntegrity();
        return result;
    }
    public byte[] MaterializePdf() { ValidateIntegrity(); return Convert.FromBase64String(PdfBase64); }
    public void ValidateIntegrity()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(PackageId.Value) ||
            !ReviewerPackagePresentationSnapshot.IsHash(PresentationSha256) ||
            string.IsNullOrWhiteSpace(ConverterIdentity) || string.IsNullOrWhiteSpace(ConverterVersion) ||
            !ReviewerPackagePresentationSnapshot.IsHash(PdfSha256) ||
            !string.Equals(Sha256, ComputeHash(), StringComparison.Ordinal))
            throw new InvalidDataException("Frozen reviewer PDF integrity failure.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(PdfBase64); }
        catch (FormatException ex) { throw new InvalidDataException("Invalid prepared PDF encoding.", ex); }
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8) ||
            !string.Equals(PdfSha256, ReviewerPackagePresentationSnapshot.Hash(bytes), StringComparison.Ordinal))
            throw new InvalidDataException("Frozen PDF integrity failure.");
    }
    private string ComputeHash() => ReviewerPackagePresentationSnapshot.Hash(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(this with { Sha256 = string.Empty })));
}

internal sealed class ReviewerPresentationPackageIdJsonConverter : JsonConverter<EvidencePackageId>
{
    public override EvidencePackageId Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException("Package identity is missing."));
    public override void Write(Utf8JsonWriter writer, EvidencePackageId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
