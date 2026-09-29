using System.Security.Cryptography;
using System.Text.Json;
using EMF.Common;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageDocumentOutputService
{
    private readonly IEvidencePackageRepository? _snapshotRepository;
    private readonly IVeteransReviewerPackageDocumentConverter? _converter;
    private readonly IVeteransReviewerRegulatoryTextProvider? _regulatoryTextProvider;
    private readonly Func<EmfVerifiedFirstPartyDeploymentIdentity>? _verifyDeployment;

    public VeteransReviewerPackageDocumentOutputService(
        IVeteransReviewerPackageDocumentConverter? converter = null,
        IVeteransReviewerRegulatoryTextProvider? regulatoryTextProvider = null,
        IEvidencePackageRepository? snapshotRepository = null)
    {
        _snapshotRepository = snapshotRepository;
        _converter = converter;
        _regulatoryTextProvider = regulatoryTextProvider;
    }

    public VeteransReviewerPackageDocumentOutputService(
        IVeteransReviewerPackageDocumentConverter? converter,
        IVeteransReviewerRegulatoryTextProvider? regulatoryTextProvider,
        IEvidencePackageRepository? snapshotRepository,
        EmfBuildManifest? buildManifest)
        : this(converter, regulatoryTextProvider, snapshotRepository)
    {
        if (buildManifest is not null)
            EmfBuildManifestIdentity.Validate(buildManifest);
    }

    [Obsolete("Use CreateForVerifiedDeployment for new M92 generation. Runtime tokens do not authorize deployment attribution.")]
    public VeteransReviewerPackageDocumentOutputService(
        IVeteransReviewerPackageDocumentConverter? converter,
        IVeteransReviewerRegulatoryTextProvider? regulatoryTextProvider,
        IEvidencePackageRepository? snapshotRepository,
        EmfBuildManifest? buildManifest,
        EmfVerifiedRuntimeIdentity? verifiedRuntimeIdentity)
        : this(converter, regulatoryTextProvider, snapshotRepository, buildManifest)
    {
        if (verifiedRuntimeIdentity is not null)
        {
            EmfBuildManifestIdentity.Validate(
                verifiedRuntimeIdentity.Manifest);

            if (buildManifest is null ||
                !string.Equals(
                    buildManifest.BuildId,
                    verifiedRuntimeIdentity.BuildId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    buildManifest.SourceRevisionId,
                    verifiedRuntimeIdentity.SourceRevisionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Verified runtime identity does not match the supplied build manifest.");
            }

            VeteransReviewerPackageRendererIdentity
                .ValidateVerifiedRuntime(verifiedRuntimeIdentity);
        }
    }

    private VeteransReviewerPackageDocumentOutputService(
        IVeteransReviewerPackageDocumentConverter? converter,
        IVeteransReviewerRegulatoryTextProvider? regulatoryTextProvider,
        IEvidencePackageRepository? snapshotRepository,
        Func<EmfVerifiedFirstPartyDeploymentIdentity> verifyDeployment)
        : this(converter, regulatoryTextProvider, snapshotRepository)
    {
        _verifyDeployment = verifyDeployment;
    }

    /// <summary>
    /// The trusted host supplies verification against a pre-existing deployment
    /// expectation. Invoked once per generation, after M91 verified-reuse exits.
    /// </summary>
    public static VeteransReviewerPackageDocumentOutputService CreateForVerifiedDeployment(
        Func<EmfVerifiedFirstPartyDeploymentIdentity> verifyDeployment,
        IVeteransReviewerPackageDocumentConverter? converter = null,
        IVeteransReviewerRegulatoryTextProvider? regulatoryTextProvider = null,
        IEvidencePackageRepository? snapshotRepository = null)
    {
        ArgumentNullException.ThrowIfNull(verifyDeployment);
        return new(converter, regulatoryTextProvider, snapshotRepository, verifyDeployment);
    }

    public async Task<VeteransReviewerPackageDocumentOutput> RenderAsync(
        VeteransReviewerPackageDetails details,
        VeteransReviewerPackageOutputFormat format,
        CancellationToken cancellationToken = default,
        ReviewerPackageSnapshot? preparedSnapshot = null,
        DateOnly? sourceReviewDate = null,
        VeteransReviewerPackageExistingOutput? existingOutput = null)
    {
        ArgumentNullException.ThrowIfNull(details);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(format))
            throw new InvalidOperationException("Unsupported reviewer-package output format.");

        var stored = _snapshotRepository is null ? null :
            await _snapshotRepository.GetReviewerSnapshotAsync(
                details.PackageDetails.Package.Id,
                cancellationToken);

        if (preparedSnapshot is not null)
        {
            preparedSnapshot.ValidateIntegrity();
            if (preparedSnapshot.PackageId != details.PackageDetails.Package.Id ||
                (stored is not null && stored != preparedSnapshot))
            {
                throw new InvalidDataException(
                    "Prepared reviewer output no longer matches the package snapshot.");
            }
        }

        var captured = stored is null && _snapshotRepository is not null
            ? preparedSnapshot ?? await CaptureCurrentAsync(details, cancellationToken)
            : null;

        var selected = stored ?? preparedSnapshot ?? captured;
        var restored = selected is null ? null : VeteransReviewerPackageSnapshot.Restore(selected);
        if (restored is not null)
            details = restored.Details;

        var regulations =
            restored?.Regulations ??
            await GetApplicableRegulationsAsync(details, cancellationToken);

        var reviewDate =
            sourceReviewDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var requiresDocx =
            format is VeteransReviewerPackageOutputFormat.Docx or
                VeteransReviewerPackageOutputFormat.Both;

        var requiresPdf =
            format is VeteransReviewerPackageOutputFormat.Pdf or
                VeteransReviewerPackageOutputFormat.Both;

        if (requiresPdf && _converter is null)
        {
            throw new InvalidOperationException(
                "PDF reviewer-package output requires a DOCX-to-PDF converter.");
        }

        var provenanceEnabled =
            _snapshotRepository?.SupportsReviewerOutputProvenance == true &&
            selected is not null;

        var buildProvenanceEnabled =
            provenanceEnabled &&
            _snapshotRepository?.SupportsReviewerOutputBuildProvenance == true;

        VeteransReviewerPackageDocumentConverterInfo? converterInfo = null;
        if (requiresPdf && provenanceEnabled && existingOutput?.Pdf is not null)
            converterInfo = await RequireConverterInfoAsync();

        IReadOnlyList<ReviewerPackageOutputProvenance> provenanceRows =
            !provenanceEnabled
                ? Array.Empty<ReviewerPackageOutputProvenance>()
                : await _snapshotRepository!.GetReviewerOutputProvenanceAsync(
                    selected!.PackageId,
                    cancellationToken);

        var reusablePdf = requiresPdf
            ? VerifyExistingOutput(
                ReviewerPackageOutputFormats.Pdf,
                existingOutput?.Pdf,
                converterInfo)
            : null;

        var needsDocx = requiresDocx || reusablePdf is null && requiresPdf;
        var reusableDocx = needsDocx
            ? VerifyExistingOutput(
                ReviewerPackageOutputFormats.Docx,
                existingOutput?.Docx,
                converterInfo: null)
            : null;

        if (format == VeteransReviewerPackageOutputFormat.Docx && reusableDocx is not null)
        {
            return new VeteransReviewerPackageDocumentOutput(
                reusableDocx,
                null,
                ReusedDocx: true);
        }

        if (format == VeteransReviewerPackageOutputFormat.Pdf && reusablePdf is not null)
        {
            ValidatePdf(reusablePdf);
            return new VeteransReviewerPackageDocumentOutput(
                null,
                reusablePdf,
                ReusedPdf: true);
        }

        if (format == VeteransReviewerPackageOutputFormat.Both &&
            reusableDocx is not null &&
            reusablePdf is not null)
        {
            ValidatePdf(reusablePdf);
            return new VeteransReviewerPackageDocumentOutput(
                reusableDocx,
                reusablePdf,
                ReusedDocx: true,
                ReusedPdf: true);
        }

        EmfVerifiedFirstPartyDeploymentIdentity? deploymentIdentity = null;
        if (buildProvenanceEnabled)
        {
            if (_verifyDeployment is null)
                throw new InvalidOperationException(
                    "Reviewer output generation requires a build manifest and verified " +
                    "first-party deployment identity when build provenance is supported.");

            deploymentIdentity = _verifyDeployment()
                ?? throw new InvalidDataException("Deployment verification returned no identity.");
            VeteransReviewerPackageRendererIdentity.ValidateVerifiedDeployment(deploymentIdentity);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var docx = reusableDocx ??
            VeteransReviewerPackageDocxRenderer.Render(
                details,
                regulations,
                reviewDate);

        cancellationToken.ThrowIfCancellationRequested();

        byte[]? pdf = reusablePdf;
        if (requiresPdf && pdf is null)
        {
            pdf = await _converter!.ConvertDocxToPdfAsync(
                docx,
                cancellationToken);
        }

        if (pdf is not null)
            ValidatePdf(pdf);

        if (requiresPdf &&
            reusablePdf is null &&
            provenanceEnabled &&
            converterInfo is null)
        {
            converterInfo = await RequireConverterInfoAsync();
        }

        await SealAsync();

        if (requiresDocx && reusableDocx is null)
        {
            await SaveProvenanceAsync(
                ReviewerPackageOutputFormats.Docx,
                docx,
                converterInfo: null);
        }

        if (requiresPdf && reusablePdf is null)
        {
            await SaveProvenanceAsync(
                ReviewerPackageOutputFormats.Pdf,
                pdf!,
                converterInfo);
        }

        return format switch
        {
            VeteransReviewerPackageOutputFormat.Docx =>
                new VeteransReviewerPackageDocumentOutput(
                    docx,
                    null,
                    ReusedDocx: reusableDocx is not null),
            VeteransReviewerPackageOutputFormat.Pdf =>
                new VeteransReviewerPackageDocumentOutput(
                    null,
                    pdf,
                    ReusedPdf: reusablePdf is not null),
            VeteransReviewerPackageOutputFormat.Both =>
                new VeteransReviewerPackageDocumentOutput(
                    docx,
                    pdf,
                    ReusedDocx: reusableDocx is not null,
                    ReusedPdf: reusablePdf is not null),
            _ => throw new InvalidOperationException(
                "Unsupported reviewer-package output format.")
        };

        async Task SealAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (captured is not null)
            {
                await _snapshotRepository!.SaveReviewerSnapshotAsync(
                    captured,
                    details.PackageDetails,
                    cancellationToken);
            }
        }

        byte[]? VerifyExistingOutput(
            string outputFormat,
            byte[]? candidate,
            VeteransReviewerPackageDocumentConverterInfo? converterInfo)
        {
            if (candidate is null ||
                selected is null ||
                provenanceRows.Count == 0)
            {
                return null;
            }

            var eligible =
                provenanceRows
                    .Where(row =>
                        string.Equals(row.Format, outputFormat, StringComparison.Ordinal) &&
                        string.Equals(row.SnapshotSha256, selected.Sha256, StringComparison.Ordinal) &&
                        string.Equals(
                            row.RendererContract,
                            VeteransReviewerPackageRendererIdentity.Contract,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            row.RendererBuild,
                            VeteransReviewerPackageRendererIdentity.Build,
                            StringComparison.Ordinal) &&
                        row.SourceReviewDate == reviewDate &&
                        string.Equals(
                            row.ConverterIdentity,
                            converterInfo?.Identity,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            row.ConverterVersion,
                            converterInfo?.Version,
                            StringComparison.Ordinal))
                    .ToArray();

            if (eligible.Length == 0)
                return null;

            var candidateHash =
                Convert.ToHexString(SHA256.HashData(candidate));

            if (eligible.Any(row =>
                    row.ByteLength == candidate.LongLength &&
                    string.Equals(
                        row.OutputSha256,
                        candidateHash,
                        StringComparison.Ordinal)))
            {
                return candidate;
            }

            throw new InvalidDataException(
                "Existing reviewer output does not match persisted provenance " +
                "for the current sealed snapshot and renderer identity.");
        }

        async Task<VeteransReviewerPackageDocumentConverterInfo> RequireConverterInfoAsync()
        {
            if (_converter is not IVeteransReviewerPackageDocumentConverterInfoProvider infoProvider)
            {
                throw new InvalidOperationException(
                    "PDF reviewer-package provenance requires converter identity metadata.");
            }

            var info =
                await infoProvider.GetDocumentConverterInfoAsync(cancellationToken);
            info.Validate();
            return info;
        }

        async Task SaveProvenanceAsync(
            string outputFormat,
            ReadOnlyMemory<byte> output,
            VeteransReviewerPackageDocumentConverterInfo? converterInfo)
        {
            if (!provenanceEnabled)
                return;

            if (selected is null)
            {
                throw new InvalidDataException(
                    "Reviewer output provenance requires a sealed reviewer snapshot.");
            }

            var provenance =
                ReviewerPackageOutputProvenance.Create(
                    selected.PackageId,
                    outputFormat,
                    selected.Sha256,
                    VeteransReviewerPackageRendererIdentity.Contract,
                    VeteransReviewerPackageRendererIdentity.Build,
                    converterInfo?.Identity,
                    converterInfo?.Version,
                    reviewDate,
                    output,
                    DateTimeOffset.UtcNow);

            if (!buildProvenanceEnabled)
            {
                await _snapshotRepository!.SaveReviewerOutputProvenanceAsync(
                    provenance,
                    cancellationToken);
                return;
            }

            var buildProvenance =
                ReviewerPackageOutputBuildProvenance.Create(
                    provenance.ProvenanceId,
                    deploymentIdentity!.BuildId,
                    deploymentIdentity.SourceRevisionId,
                    DateTimeOffset.UtcNow);

            await _snapshotRepository!
                .SaveReviewerOutputWithBuildProvenanceAndManifestAsync(
                    provenance,
                    buildProvenance,
                    JsonSerializer.Serialize(deploymentIdentity.Manifest),
                    cancellationToken);
        }
    }

    public async Task<ReviewerPackageSnapshot> CaptureCurrentAsync(
        VeteransReviewerPackageDetails details,
        CancellationToken cancellationToken = default) =>
        VeteransReviewerPackageSnapshot.Capture(
            details,
            await GetApplicableRegulationsAsync(details, cancellationToken));

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
