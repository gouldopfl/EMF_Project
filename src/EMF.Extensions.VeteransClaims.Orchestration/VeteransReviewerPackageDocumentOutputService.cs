using EMF.Extensions.VeteransClaims.Contracts;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageDocumentOutputService
{
    private readonly IEvidencePackageRepository? _snapshotRepository;
    private readonly IVeteransReviewerPackageDocumentConverter? _converter;
    private readonly IVeteransReviewerRegulatoryTextProvider? _regulatoryTextProvider;

    public VeteransReviewerPackageDocumentOutputService(
        IVeteransReviewerPackageDocumentConverter? converter = null,
        IVeteransReviewerRegulatoryTextProvider? regulatoryTextProvider = null,
        IEvidencePackageRepository? snapshotRepository = null)
    {
        _snapshotRepository = snapshotRepository;
        _converter = converter;
        _regulatoryTextProvider = regulatoryTextProvider;
    }

    public async Task<VeteransReviewerPackageDocumentOutput> RenderAsync(
        VeteransReviewerPackageDetails details,
        VeteransReviewerPackageOutputFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(format)) throw new InvalidOperationException("Unsupported reviewer-package output format.");

        var stored = _snapshotRepository is null ? null :
            await _snapshotRepository.GetReviewerSnapshotAsync(details.PackageDetails.Package.Id, cancellationToken);
        var restored = stored is null ? null : VeteransReviewerPackageSnapshot.Restore(stored);
        if (restored is not null) details = restored.Details;
        var regulations = restored?.Regulations ?? await GetApplicableRegulationsAsync(details, cancellationToken);
        var captured = _snapshotRepository is not null && stored is null
            ? VeteransReviewerPackageSnapshot.Capture(details, regulations) : null;
        // Render the deserialized copy so the first output and later outputs use
        // identical types (including metadata JsonElements) and frozen inputs.
        if (captured is not null)
        {
            restored = VeteransReviewerPackageSnapshot.Restore(captured);
            details = restored.Details;
            regulations = restored.Regulations;
        }
        var docx = VeteransReviewerPackageDocxRenderer.Render(details, regulations);

        cancellationToken.ThrowIfCancellationRequested();

        if (format == VeteransReviewerPackageOutputFormat.Docx)
        {
            await SealAsync();
            return new VeteransReviewerPackageDocumentOutput(
                docx,
                null);
        }

        if (_converter is null)
        {
            throw new InvalidOperationException(
                "PDF reviewer-package output requires a DOCX-to-PDF converter.");
        }

        var pdf =
            await _converter.ConvertDocxToPdfAsync(
                docx,
                cancellationToken);

        ValidatePdf(pdf);
        await SealAsync();

        return format switch
        {
            VeteransReviewerPackageOutputFormat.Pdf =>
                new VeteransReviewerPackageDocumentOutput(
                    null,
                    pdf),
            VeteransReviewerPackageOutputFormat.Both =>
                new VeteransReviewerPackageDocumentOutput(
                    docx,
                    pdf),
            _ => throw new InvalidOperationException(
                "Unsupported reviewer-package output format.")
        };
        async Task SealAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (captured is not null)
                await _snapshotRepository!.SaveReviewerSnapshotAsync(captured, details.PackageDetails, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<VeteransReviewerApplicableRegulation>>
        GetApplicableRegulationsAsync(
            VeteransReviewerPackageDetails details,
            CancellationToken cancellationToken)
    {
        var citations =
            details.MedicalOpinionRequested?
                .ApplicableRegulatoryCitations ?? [];

        if (citations.Count == 0)
            return [];

        if (_regulatoryTextProvider is null)
        {
            throw new InvalidOperationException(
                "Reviewer package contains applicable regulatory citations, " +
                "but no regulatory text provider is configured.");
        }

        var regulations =
            await _regulatoryTextProvider.GetCurrentAsync(
                citations,
                cancellationToken);

        var expected =
            citations
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var actual =
            regulations
                .Select(value => value.Citation.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (!expected.SequenceEqual(
                actual,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Regulatory text lookup did not return the complete set of " +
                "applicable reviewer citations.");
        }

        return regulations;
    }

    private static void ValidatePdf(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        if (pdf.Length < 5 ||
            pdf[0] != (byte)'%' ||
            pdf[1] != (byte)'P' ||
            pdf[2] != (byte)'D' ||
            pdf[3] != (byte)'F' ||
            pdf[4] != (byte)'-')
        {
            throw new InvalidDataException(
                "DOCX-to-PDF conversion did not produce a valid PDF document.");
        }
    }
}
