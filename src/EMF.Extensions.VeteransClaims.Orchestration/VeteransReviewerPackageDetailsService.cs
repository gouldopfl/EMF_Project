using System.Text;
using System.Text.Json;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageDetailsService
{
    private readonly IEvidencePackageService _packages;
    private readonly IEvidenceRepository _evidence;
    private readonly IEvidenceClassificationRepository? _classifications;
    private readonly IMedicalLiteratureRepository? _medicalLiterature;
    private readonly IArtifactTextExtractor? _textExtractor;
    private readonly IArtifactPrintRenderer? _printRenderer;

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(evidence);

        _packages = packages;
        _evidence = evidence;
    }

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence,
        IEvidenceClassificationRepository classifications,
        IMedicalLiteratureRepository? medicalLiterature = null)
        : this(packages, evidence)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        _classifications = classifications;
        _medicalLiterature = medicalLiterature;
    }

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence,
        IArtifactTextExtractor textExtractor)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(textExtractor);

        _packages = packages;
        _evidence = evidence;
        _textExtractor = textExtractor;
    }

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence,
        IEvidenceClassificationRepository classifications,
        IArtifactTextExtractor textExtractor,
        IMedicalLiteratureRepository? medicalLiterature = null)
        : this(packages, evidence, textExtractor)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        _classifications = classifications;
        _medicalLiterature = medicalLiterature;
    }

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence,
        IEvidenceClassificationRepository classifications,
        IArtifactTextExtractor textExtractor,
        IArtifactPrintRenderer printRenderer,
        IMedicalLiteratureRepository? medicalLiterature = null)
        : this(
            packages,
            evidence,
            classifications,
            textExtractor)
    {
        ArgumentNullException.ThrowIfNull(printRenderer);
        _printRenderer = printRenderer;
        _medicalLiterature = medicalLiterature;
    }

    public async Task<VeteransReviewerPackageDetails?> GetAsync(
        EvidencePackageId packageId,
        CancellationToken cancellationToken = default)
    {
        var details =
            await _packages.GetAsync(
                packageId,
                cancellationToken);

        if (details is null)
            return null;

        var artifacts = new List<Artifact>();
        var artifactContents =
            new List<VeteransReviewerArtifactContent>();

        foreach (var packageArtifact in details.Artifacts)
        {
            var artifact =
                await _evidence.GetArtifactAsync(
                    packageArtifact.ArtifactId,
                    cancellationToken);

            if (artifact is null)
            {
                throw new InvalidOperationException(
                    $"Evidence package '{packageId.Value}' references " +
                    $"missing artifact '{packageArtifact.ArtifactId.Value}'.");
            }

            if (artifact.Id != packageArtifact.ArtifactId)
                throw new InvalidOperationException(
                    "Evidence package artifact identity mismatch.");

            artifacts.Add(artifact);

            var isOscar =
                artifact.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) &&
                artifact.Name.Contains("OSCAR", StringComparison.OrdinalIgnoreCase);

            var text =
                GetTextSummary(artifact);

            if ((isOscar || string.IsNullOrWhiteSpace(text)) &&
                _textExtractor is not null)
            {
                text =
                    await _textExtractor.ExtractTextAsync(
                        artifact.Id,
                        cancellationToken);
            }

            if (isOscar)
            {
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException(
                        "OSCAR evidence has no extractable session data.");

                text = PapTherapyReviewerFormatter.Format(
                    Encoding.UTF8.GetBytes(text));
            }

            IReadOnlyList<PrintableArtifactPage> printablePages = [];

            if (string.Equals(
                    packageArtifact.ContentRole,
                    Models.Adjudication.EvidencePackageContentRoles
                        .UnderlyingEvidence,
                    StringComparison.Ordinal) &&
                _printRenderer is not null)
            {
                printablePages =
                    isOscar
                        ? [new PrintableArtifactPage
                        {
                            PageNumber = 1,
                            ContentType = "text/plain",
                            Content = Encoding.UTF8.GetBytes(text!)
                        }]
                        : await _printRenderer.RenderAsync(
                            artifact.Id,
                            cancellationToken);

                if (printablePages.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Underlying evidence artifact '{artifact.Id.Value}' " +
                        "has no printable source representation.");
                }
            }

            var provenance =
                await _evidence.GetProvenanceAsync(
                    artifact.Id,
                    cancellationToken);

            if (provenance.Any(
                    item => item.ArtifactId != artifact.Id))
            {
                throw new InvalidOperationException(
                    $"Artifact '{artifact.Id.Value}' provenance lookup " +
                    "returned a different artifact.");
            }

            var relationships =
                await _evidence.GetRelationshipsAsync(
                    artifact.Id,
                    cancellationToken);

            if (relationships.Any(
                    relationship =>
                        relationship.SourceArtifactId != artifact.Id &&
                        relationship.TargetArtifactId != artifact.Id))
            {
                throw new InvalidOperationException(
                    $"Artifact '{artifact.Id.Value}' relationship lookup " +
                    "returned an unrelated artifact relationship.");
            }

            var appendix =
                await GetAppendixAsync(
                    artifact.Id,
                    cancellationToken);

            var reviewerArtifact =
                await GetReviewerArtifactAsync(
                    artifact,
                    appendix,
                    cancellationToken);

            var reviewedMedicalLiteratureClassifications =
                await GetReviewedMedicalLiteratureClassificationsAsync(
                    artifact.Id,
                    appendix,
                    cancellationToken);

            artifactContents.Add(
                new VeteransReviewerArtifactContent
                {
                    Artifact = reviewerArtifact,
                    Text = text ?? string.Empty,
                    PrintablePages = printablePages,
                    Provenance = provenance,
                    Relationships = relationships,
                    Appendix = appendix,
                    ReviewedMedicalLiteratureClassifications =
                        reviewedMedicalLiteratureClassifications
                });
        }

        return new VeteransReviewerPackageDetails
        {
            PackageDetails = details,
            Artifacts = artifacts,
            ArtifactContents = artifactContents
        };
    }

    private async Task<
        IReadOnlyList<ReviewedMedicalLiteratureClassification>>
        GetReviewedMedicalLiteratureClassificationsAsync(
            EMF.Core.Models.Identities.ArtifactId artifactId,
            string? appendix,
            CancellationToken cancellationToken)
    {
        if (_medicalLiterature is null ||
            appendix != VeteransReviewerPackageAppendix.MedicalLiterature)
        {
            return [];
        }

        IReadOnlyList<ReviewedMedicalLiteratureClassification> reviewed;

        try
        {
            reviewed =
                await _medicalLiterature.GetReviewedClassificationsAsync(
                    artifactId,
                    cancellationToken);
        }
        catch (NotSupportedException)
        {
            return [];
        }

        if (reviewed.Any(item => item.ArtifactId != artifactId))
        {
            throw new InvalidOperationException(
                $"Medical literature artifact '{artifactId.Value}' reviewed " +
                "classification lookup returned a different artifact.");
        }

        if (reviewed.Any(
                item =>
                    item.SourceExcerpts.Any(
                        excerpt => excerpt.ArtifactId != artifactId)))
        {
            throw new InvalidOperationException(
                $"Medical literature artifact '{artifactId.Value}' reviewed " +
                "classification excerpt identity mismatch.");
        }

        var sourceIds =
            await _medicalLiterature.GetMedicalLiteratureSourceIdsAsync(
                artifactId,
                cancellationToken);

        if (reviewed.Any(
                item =>
                    !sourceIds.Contains(
                        item.Association.MedicalLiteratureSourceId)))
        {
            throw new InvalidOperationException(
                $"Medical literature artifact '{artifactId.Value}' reviewed " +
                "classification source identity mismatch.");
        }

        return reviewed
            .OrderBy(
                item => item.Association.RequirementId.Value,
                StringComparer.Ordinal)
            .ThenBy(
                item => item.Association.GuidanceRole,
                StringComparer.Ordinal)
            .ThenBy(item => item.ReviewedUtc)
            .ToArray();
    }

    private async Task<Artifact> GetReviewerArtifactAsync(
        Artifact artifact,
        string? appendix,
        CancellationToken cancellationToken)
    {
        if (_medicalLiterature is null ||
            appendix != VeteransReviewerPackageAppendix.MedicalLiterature)
            return artifact;

        var sourceIds =
            await _medicalLiterature.GetMedicalLiteratureSourceIdsAsync(
                artifact.Id,
                cancellationToken);

        if (sourceIds.Count != 1)
            throw new InvalidOperationException(
                $"Medical literature artifact '{artifact.Id.Value}' " +
                "must map to exactly one literature source.");

        var source =
            await _medicalLiterature.GetMedicalLiteratureSourceAsync(
                sourceIds[0],
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Medical literature source '{sourceIds[0].Value}' " +
                "could not be read.");

        if (source.Id != sourceIds[0])
            throw new InvalidOperationException(
                "Medical literature source identity mismatch.");

        var metadata =
            new Dictionary<string, object>(artifact.Metadata)
            {
                [EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.EvidenceTitle] =
                    source.Title
            };

        if (source.PublicationYear is not null)
        {
            metadata[
                EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.EvidenceDate] =
                    source.PublicationYear.Value.ToString();
        }

        metadata[
            EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.LiteratureAuthors] =
                source.Authors;

        metadata[
            EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.LiteraturePublication] =
                source.Publication;

        if (!string.IsNullOrWhiteSpace(source.Doi))
            metadata[
                EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.LiteratureDoi] =
                    source.Doi;

        if (!string.IsNullOrWhiteSpace(source.Pmid))
            metadata[
                EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.LiteraturePmid] =
                    source.Pmid;

        return new Artifact
        {
            Id = artifact.Id,
            Name = artifact.Name,
            ArtifactType = artifact.ArtifactType,
            Fingerprint = artifact.Fingerprint,
            CreatedUtc = artifact.CreatedUtc,
            Metadata = metadata
        };
    }

    private async Task<string?> GetAppendixAsync(
        EMF.Core.Models.Identities.ArtifactId artifactId,
        CancellationToken cancellationToken)
    {
        if (_medicalLiterature is not null)
        {
            var literatureSourceIds =
                await _medicalLiterature
                    .GetMedicalLiteratureSourceIdsAsync(
                        artifactId,
                        cancellationToken);

            if (literatureSourceIds.Count > 0)
                return VeteransReviewerPackageAppendix.MedicalLiterature;
        }

        if (_classifications is null)
            return null;

        var classifications =
            await _classifications.GetEvidenceClassificationsAsync(
                artifactId,
                cancellationToken);

        if (classifications.Any(x => x.ArtifactId != artifactId))
            throw new InvalidOperationException(
                $"Artifact '{artifactId.Value}' classification lookup returned a different artifact.");

        var appendixes =
            classifications
            .Select(x =>
                VeteransReviewerPackageAppendix.GetAppendix(
                    x.Classification))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return appendixes.Length switch
        {
            0 => null,
            1 => appendixes[0],
            _ => throw new InvalidOperationException(
                $"Artifact '{artifactId.Value}' maps to multiple reviewer appendixes.")
        };
    }

    private static string? GetTextSummary(
        Artifact artifact)
    {
        if (!string.Equals(
                artifact.ArtifactType,
                "text-summary",
                StringComparison.Ordinal))
        {
            return null;
        }

        if (!artifact.Metadata.TryGetValue(
                "summary",
                out var value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement json when
                json.ValueKind == JsonValueKind.String =>
                json.GetString(),
            _ => null
        };
    }
}
