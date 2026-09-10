using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

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

        foreach (var artifactId in classifications
                     .Select(x => x.ArtifactId)
                     .Distinct())
        {
            if (seen.Add(artifactId))
                artifactIds.Add(artifactId);
        }

        foreach (var requirement in details.Requirements)
        {
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

                foreach (var artifactId in literatureArtifactIds)
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

            sources.Add(
                new VeteransReviewerEvidenceSource
                {
                    ArtifactId = artifactId,
                    ArtifactName = artifact.Name,
                    ArtifactType = artifact.ArtifactType,
                    ContentRole =
                        EvidencePackageContentRoles.UnderlyingEvidence,
                    Classifications =
                        classifications
                            .Where(x => x.ArtifactId == artifactId)
                            .Select(x => x.Classification)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                    Text = text
                });
        }

        return sources;
    }
}
