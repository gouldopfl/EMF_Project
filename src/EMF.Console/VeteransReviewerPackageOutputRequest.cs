using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.ConsoleApplication;

internal sealed record VeteransReviewerPackageOutputRequest(
    VeteransReviewerPackageOutputFormat Format,
    string? DocxPath,
    string? PdfPath)
{
    public bool RequiresPdf => PdfPath is not null;

    public IEnumerable<string> OutputPaths
    {
        get
        {
            if (DocxPath is not null)
                yield return DocxPath;

            if (PdfPath is not null)
                yield return PdfPath;
        }
    }
}

internal static class VeteransReviewerPackageOutputRequestResolver
{
    public static VeteransReviewerPackageOutputRequest Resolve(
        string outputPath,
        string? explicitFormat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var format =
            string.IsNullOrWhiteSpace(explicitFormat)
                ? InferFormat(outputPath)
                : ParseFormat(explicitFormat);

        var normalizedOutputPath = outputPath.Trim();
        var extension = Path.GetExtension(normalizedOutputPath);

        return format switch
        {
            VeteransReviewerPackageOutputFormat.Docx =>
                ResolveSingle(
                    normalizedOutputPath,
                    extension,
                    ".docx",
                    VeteransReviewerPackageOutputFormat.Docx),
            VeteransReviewerPackageOutputFormat.Pdf =>
                ResolveSingle(
                    normalizedOutputPath,
                    extension,
                    ".pdf",
                    VeteransReviewerPackageOutputFormat.Pdf),
            VeteransReviewerPackageOutputFormat.Both =>
                ResolveBoth(
                    normalizedOutputPath,
                    extension),
            _ => throw new InvalidOperationException(
                "Unsupported reviewer-package output format.")
        };
    }

    public static VeteransReviewerPackageOutputFormat ParseFormat(
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value.Trim().ToLowerInvariant() switch
        {
            "docx" => VeteransReviewerPackageOutputFormat.Docx,
            "pdf" => VeteransReviewerPackageOutputFormat.Pdf,
            "both" => VeteransReviewerPackageOutputFormat.Both,
            _ => throw new InvalidOperationException(
                "Reviewer-package output format must be docx, pdf, or both.")
        };
    }

    private static VeteransReviewerPackageOutputFormat InferFormat(
        string outputPath) =>
        string.Equals(
            Path.GetExtension(outputPath),
            ".pdf",
            StringComparison.OrdinalIgnoreCase)
            ? VeteransReviewerPackageOutputFormat.Pdf
            : VeteransReviewerPackageOutputFormat.Docx;

    private static VeteransReviewerPackageOutputRequest ResolveSingle(
        string outputPath,
        string extension,
        string requiredExtension,
        VeteransReviewerPackageOutputFormat format)
    {
        if (!string.IsNullOrWhiteSpace(extension) &&
            (string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)) &&
            !string.Equals(extension, requiredExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Output path extension '{extension}' conflicts with requested " +
                $"format '{format.ToString().ToLowerInvariant()}'.");
        }

        var finalPath =
            string.IsNullOrWhiteSpace(extension) &&
            format == VeteransReviewerPackageOutputFormat.Pdf
                ? outputPath + requiredExtension
                : outputPath;

        var resolved =
            VeteransConsoleCommand.ResolveGeneratedDocumentPath(
                finalPath);

        return format == VeteransReviewerPackageOutputFormat.Docx
            ? new VeteransReviewerPackageOutputRequest(
                format,
                resolved,
                null)
            : new VeteransReviewerPackageOutputRequest(
                format,
                null,
                resolved);
    }

    private static VeteransReviewerPackageOutputRequest ResolveBoth(
        string outputPath,
        string extension)
    {
        var stem =
            string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)
                ? outputPath[..^extension.Length]
                : outputPath;

        return new VeteransReviewerPackageOutputRequest(
            VeteransReviewerPackageOutputFormat.Both,
            VeteransConsoleCommand.ResolveGeneratedDocumentPath(
                stem + ".docx"),
            VeteransConsoleCommand.ResolveGeneratedDocumentPath(
                stem + ".pdf"));
    }
}
