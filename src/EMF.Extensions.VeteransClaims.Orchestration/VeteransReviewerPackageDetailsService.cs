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

            var sourceName =
                await GetReviewerSourceNameAsync(
                    artifact.Id,
                    relationships,
                    cancellationToken);

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
                    SourceName = sourceName,
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

    private async Task<string?> GetReviewerSourceNameAsync(
        EMF.Core.Models.Identities.ArtifactId artifactId,
        IReadOnlyList<Relationship> relationships,
        CancellationToken cancellationToken)
    {
        var derivedFrom =
            relationships
                .Where(
                    relationship =>
                        relationship.SourceArtifactId == artifactId &&
                        string.Equals(
                            relationship.RelationshipType,
                            RelationshipTypes.DerivedFrom,
                            StringComparison.Ordinal))
                .ToArray();

        if (derivedFrom.Length > 1)
            throw new InvalidOperationException(
                $"Reviewer evidence artifact '{artifactId.Value}' has " +
                "ambiguous source provenance.");

        if (derivedFrom.Length == 0)
            return null;

        var parent =
            await _evidence.GetArtifactAsync(
                derivedFrom[0].TargetArtifactId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Reviewer evidence source artifact " +
                $"'{derivedFrom[0].TargetArtifactId.Value}' was not found.");

        return GetHumanSourceName(parent);
    }

    private static string GetHumanSourceName(Artifact artifact)
    {
        var name = artifact.Name;

        if (name.Contains(
                "Blue-Button",
                StringComparison.OrdinalIgnoreCase) ||
            name.Contains(
                "Blue Button",
                StringComparison.OrdinalIgnoreCase))
        {
            return "VA Blue Button Report";
        }

        return name;
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

        var visited =
            new HashSet<EMF.Core.Models.Identities.ArtifactId>();
        var appendixes =
            new HashSet<string>(StringComparer.Ordinal);
        var current = artifactId;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visited.Add(current))
                throw new InvalidOperationException(
                    "Reviewer appendix supersession cycle detected.");

            var classifications =
                await _classifications.GetEvidenceClassificationsAsync(
                    current,
                    cancellationToken);

            if (classifications.Any(x => x.ArtifactId != current))
                throw new InvalidOperationException(
                    $"Artifact '{current.Value}' classification lookup " +
                    "returned a different artifact.");

            foreach (var classification in classifications)
            {
                appendixes.Add(
                    VeteransReviewerPackageAppendix.GetAppendix(
                        classification.Classification));
            }

            var relationships =
                await _evidence.GetRelationshipsAsync(
                    current,
                    cancellationToken);

            if (relationships.Any(
                    relationship =>
                        relationship.SourceArtifactId != current &&
                        relationship.TargetArtifactId != current))
            {
                throw new InvalidOperationException(
                    $"Artifact '{current.Value}' relationship lookup " +
                    "returned an unrelated artifact relationship.");
            }

            var predecessors =
                relationships
                    .Where(
                        relationship =>
                            relationship.SourceArtifactId == current &&
                            string.Equals(
                                relationship.RelationshipType,
                                RelationshipTypes.Supersedes,
                                StringComparison.Ordinal))
                    .ToArray();

            if (predecessors.Length == 0)
                break;

            if (predecessors.Length > 1)
                throw new InvalidOperationException(
                    $"Artifact '{current.Value}' has invalid multiple " +
                    "supersession predecessors.");

            current = predecessors[0].TargetArtifactId;
        }

        return appendixes.Count switch
        {
            0 => null,
            1 => appendixes.Single(),
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
