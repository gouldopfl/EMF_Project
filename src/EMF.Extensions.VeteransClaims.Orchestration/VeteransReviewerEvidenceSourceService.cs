using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Orchestration.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerEvidenceSourceService
{
    private readonly IEvidenceRepository _evidence;
    private readonly IMedicalLiteratureRepository _medicalLiterature;
    private readonly IArtifactTextExtractor _textExtractor;

    public VeteransReviewerEvidenceSourceService(
        IEvidenceRepository evidence,
        IMedicalLiteratureRepository medicalLiterature,
        IArtifactTextExtractor textExtractor)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(medicalLiterature);
        ArgumentNullException.ThrowIfNull(textExtractor);

        _evidence = evidence;
        _medicalLiterature = medicalLiterature;
        _textExtractor = textExtractor;
    }

    public async Task<IReadOnlyList<VeteransReviewerEvidenceSource>>
        GetAsync(
            ClaimIssueAdjudicationDetails details,
            IReadOnlyList<EvidenceClassification> classifications,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(classifications);

        foreach (var classification in classifications)
        {
            if (classification.ClaimIssueId != details.ClaimIssue.Id)
            {
                throw new InvalidOperationException(
                    "Reviewer evidence classification claim issue mismatch.");
            }
        }

        var artifactIds = new List<ArtifactId>();
        var seen = new HashSet<ArtifactId>();
        var classificationsByArtifact =
            new Dictionary<ArtifactId, HashSet<string>>();
        var reviewedLiteratureByArtifact =
            new Dictionary<
                ArtifactId,
                List<ReviewedMedicalLiteratureClassification>>();
        var supersession =
            new ArtifactSupersessionService(_evidence);

        foreach (var classificationGroup in
                 classifications.GroupBy(x => x.ArtifactId))
        {
            var activeArtifactId =
                await supersession.ResolveActiveArtifactIdAsync(
                    classificationGroup.Key,
                    cancellationToken);

            if (seen.Add(activeArtifactId))
                artifactIds.Add(activeArtifactId);

            if (!classificationsByArtifact.TryGetValue(
                    activeArtifactId,
                    out var activeClassifications))
            {
                activeClassifications = new HashSet<string>(
                    StringComparer.Ordinal);
                classificationsByArtifact.Add(
                    activeArtifactId,
                    activeClassifications);
            }

            foreach (var classification in classificationGroup)
                activeClassifications.Add(classification.Classification);
        }

        foreach (var requirement in details.Requirements)
        {
            IReadOnlyList<ReviewedMedicalLiteratureClassification>
                reviewedClassifications;

            try
            {
                reviewedClassifications =
                    await _medicalLiterature.GetReviewedClassificationsAsync(
                        requirement.Requirement.Id,
                        cancellationToken);
            }
            catch (NotSupportedException)
            {
                reviewedClassifications = [];
            }

            if (reviewedClassifications.Any(
                    classification =>
                        classification.Association.RequirementId !=
                        requirement.Requirement.Id))
            {
                throw new InvalidOperationException(
                    "Reviewer medical literature reviewed classification " +
                    "requirement mismatch.");
            }

            foreach (var literature in requirement.MedicalLiterature)
            {
                if (literature.Association.RequirementId !=
                    requirement.Requirement.Id)
                {
                    throw new InvalidOperationException(
                        "Reviewer medical literature requirement mismatch.");
                }

                if (literature.Source.Id !=
                    literature.Association.MedicalLiteratureSourceId)
                {
                    throw new InvalidOperationException(
                        "Reviewer medical literature source identity mismatch.");
                }

                var literatureArtifactIds =
                    await _medicalLiterature.GetArtifactIdsAsync(
                        literature.Source.Id,
                        cancellationToken);

                var literatureArtifactIdSet =
                    literatureArtifactIds.ToHashSet();

                var reviewedForLiterature =
                    reviewedClassifications
                        .Where(
                            classification =>
                                classification.Association
                                    .MedicalLiteratureSourceId ==
                                literature.Source.Id &&
                                string.Equals(
                                    classification.Association.GuidanceRole,
                                    literature.Association.GuidanceRole,
                                    StringComparison.Ordinal))
                        .ToArray();

                foreach (var reviewed in reviewedForLiterature)
                {
                    if (!string.Equals(
                            reviewed.Association.Description,
                            literature.Association.Description,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Reviewer medical literature reviewed classification " +
                            "association mismatch.");
                    }

                    if (!literatureArtifactIdSet.Contains(reviewed.ArtifactId))
                    {
                        throw new InvalidOperationException(
                            "Reviewer medical literature reviewed artifact is not " +
                            "associated with the literature source.");
                    }

                    if (reviewed.SourceExcerpts.Any(
                            excerpt =>
                                excerpt.ArtifactId != reviewed.ArtifactId))
                    {
                        throw new InvalidOperationException(
                            "Reviewer medical literature reviewed excerpt artifact " +
                            "identity mismatch.");
                    }
                }

                foreach (var reviewed in reviewedForLiterature)
                {
                    if (!reviewedLiteratureByArtifact.TryGetValue(
                            reviewed.ArtifactId,
                            out var reviewedForArtifact))
                    {
                        reviewedForArtifact = [];
                        reviewedLiteratureByArtifact.Add(
                            reviewed.ArtifactId,
                            reviewedForArtifact);
                    }

                    reviewedForArtifact.Add(reviewed);
                }

                var selectedArtifactIds =
                    reviewedForLiterature.Length == 0
                        ? literatureArtifactIds
                        : reviewedForLiterature
                            .Select(classification => classification.ArtifactId)
                            .Distinct()
                            .ToArray();

                foreach (var artifactId in selectedArtifactIds)
                {
                    if (seen.Add(artifactId))
                        artifactIds.Add(artifactId);
                }
            }
        }

        var sources =
            new List<VeteransReviewerEvidenceSource>(
                artifactIds.Count);

        foreach (var artifactId in artifactIds)
        {
            var artifact =
                await _evidence.GetArtifactAsync(
                    artifactId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Reviewer evidence artifact not found: {artifactId.Value}");

            if (artifact.Id != artifactId)
            {
                throw new InvalidOperationException(
                    "Reviewer evidence artifact identity mismatch.");
            }

            var text =
                await _textExtractor.ExtractTextAsync(
                    artifactId,
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException(
                    $"Unable to extract reviewer evidence: {artifactId.Value}");
            }

            var sourceStartPage =
                GetMetadataText(
                    artifact.Metadata,
                    VeteransArtifactMetadataKeys.SourceStartPage);
            var sourceEndPage =
                GetMetadataText(
                    artifact.Metadata,
                    VeteransArtifactMetadataKeys.SourceEndPage);
            var evidenceTitle =
                GetMetadataText(
                    artifact.Metadata,
                    VeteransArtifactMetadataKeys.EvidenceTitle)
                ?? GetMetadataText(
                    artifact.Metadata,
                    VeteransArtifactMetadataKeys.NoteTitle);
            var evidenceDate =
                GetMetadataText(
                    artifact.Metadata,
                    VeteransArtifactMetadataKeys.EvidenceDate)
                ?? GetMetadataText(
                    artifact.Metadata,
                    VeteransArtifactMetadataKeys.NoteDate);

            string? sourceName = null;

            var relationships =
                await _evidence.GetRelationshipsAsync(
                    artifactId,
                    cancellationToken);

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
            {
                throw new InvalidOperationException(
                    $"Reviewer evidence artifact '{artifactId.Value}' has " +
                    "ambiguous source provenance.");
            }

            if (derivedFrom.Length == 1)
            {
                var parent =
                    await _evidence.GetArtifactAsync(
                        derivedFrom[0].TargetArtifactId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Reviewer evidence source artifact " +
                        $"'{derivedFrom[0].TargetArtifactId.Value}' was not found.");

                sourceName = GetHumanSourceName(parent);
            }

            sources.Add(
                new VeteransReviewerEvidenceSource
                {
                    ArtifactId = artifactId,
                    ArtifactName = artifact.Name,
                    ArtifactType = artifact.ArtifactType,
                    ContentRole =
                        EvidencePackageContentRoles.UnderlyingEvidence,
                    SourceName = sourceName,
                    SourceStartPage = sourceStartPage,
                    SourceEndPage = sourceEndPage,
                    EvidenceTitle = evidenceTitle,
                    EvidenceDate = evidenceDate,
                    Classifications =
                        classificationsByArtifact.TryGetValue(
                            artifactId,
                            out var activeClassifications)
                            ? activeClassifications.ToArray()
                            : [],
                    ReviewedMedicalLiteratureClassifications =
                        reviewedLiteratureByArtifact.TryGetValue(
                            artifactId,
                            out var reviewedLiterature)
                            ? reviewedLiterature.ToArray()
                            : [],
                    Text = text
                });
        }

        return sources;
    }

    private static string? GetMetadataText(
        IReadOnlyDictionary<string, object> metadata,
        string key)
    {
        if (!metadata.TryGetValue(key, out var value))
            return null;

        var text = value?.ToString();

        return string.IsNullOrWhiteSpace(text)
            ? null
            : text;
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
}
