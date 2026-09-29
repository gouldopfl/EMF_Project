using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public static class ReviewerPackageOutputFormats
{
    public const string Docx = "docx";
    public const string Pdf = "pdf";

    public static bool IsSupported(string format) =>
        string.Equals(format, Docx, StringComparison.Ordinal) ||
        string.Equals(format, Pdf, StringComparison.Ordinal);
}

/// <summary>
/// Immutable provenance for exact reviewer-package output bytes. The provenance
/// identity intentionally excludes GeneratedUtc so an exact retry is idempotent.
/// </summary>
public sealed record ReviewerPackageOutputProvenance(
    string ProvenanceId,
    EvidencePackageId PackageId,
    int Version,
    string Format,
    string SnapshotSha256,
    string RendererContract,
    string RendererBuild,
    string? ConverterIdentity,
    string? ConverterVersion,
    DateOnly SourceReviewDate,
    string OutputSha256,
    long ByteLength,
    DateTimeOffset GeneratedUtc)
{
    public static ReviewerPackageOutputProvenance Create(
        EvidencePackageId packageId,
        string format,
        string snapshotSha256,
        string rendererContract,
        string rendererBuild,
        string? converterIdentity,
        string? converterVersion,
        DateOnly sourceReviewDate,
        ReadOnlyMemory<byte> output,
        DateTimeOffset generatedUtc)
    {
        if (output.Length == 0)
            throw new InvalidDataException("Reviewer output content is empty.");

        var row = new ReviewerPackageOutputProvenance(
            string.Empty,
            packageId,
            1,
            format,
            snapshotSha256,
            rendererContract,
            rendererBuild,
            converterIdentity,
            converterVersion,
            sourceReviewDate,
            Convert.ToHexString(SHA256.HashData(output.Span)),
            output.Length,
            generatedUtc);

        row = row with { ProvenanceId = ComputeIdentity(row) };
        row.ValidateIntegrity();
        return row;
    }

    public void ValidateIntegrity()
    {
        if (Version != 1 ||
            string.IsNullOrWhiteSpace(PackageId.Value) ||
            !ReviewerPackageOutputFormats.IsSupported(Format) ||
            !IsUpperHexSha256(SnapshotSha256) ||
            string.IsNullOrWhiteSpace(RendererContract) ||
            string.IsNullOrWhiteSpace(RendererBuild) ||
            SourceReviewDate == default ||
            !IsUpperHexSha256(OutputSha256) ||
            ByteLength <= 0 ||
            GeneratedUtc == default)
        {
            throw new InvalidDataException(
                "Reviewer output provenance contains invalid required fields.");
        }

        var pdf = string.Equals(Format, ReviewerPackageOutputFormats.Pdf, StringComparison.Ordinal);
        if (pdf != (!string.IsNullOrWhiteSpace(ConverterIdentity) &&
                    !string.IsNullOrWhiteSpace(ConverterVersion)) ||
            (!pdf && (ConverterIdentity is not null || ConverterVersion is not null)))
        {
            throw new InvalidDataException(
                "Reviewer output converter identity is inconsistent with the output format.");
        }

        if (!IsUpperHexSha256(ProvenanceId) ||
            !string.Equals(ProvenanceId, ComputeIdentity(this), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Reviewer output provenance identity is invalid.");
        }
    }

    public static string ComputeIdentity(ReviewerPackageOutputProvenance value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var canonical = new StringBuilder("reviewer-output-provenance-v1\n");
        Append(canonical, value.PackageId.Value);
        Append(canonical, value.Version.ToString(CultureInfo.InvariantCulture));
        Append(canonical, value.Format);
        Append(canonical, value.SnapshotSha256);
        Append(canonical, value.RendererContract);
        Append(canonical, value.RendererBuild);
        AppendNullable(canonical, value.ConverterIdentity);
        AppendNullable(canonical, value.ConverterVersion);
        Append(canonical, value.SourceReviewDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(canonical, value.OutputSha256);
        Append(canonical, value.ByteLength.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('\n');

    private static void AppendNullable(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("null\n");
            return;
        }

        builder.Append("value:");
        Append(builder, value);
    }

    private static bool IsUpperHexSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
