using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class MedicalLiteratureClassificationValidator
{
    public void ValidateAgainstSource(
        MedicalLiteratureClassificationProposal proposal,
        MedicalLiteratureSourceId sourceId,
        ArtifactId artifactId,
        IReadOnlyList<Requirement> candidateRequirements,
        string sourceText)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(candidateRequirements);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);

        if (proposal.MedicalLiteratureSourceId != sourceId)
            throw new InvalidOperationException(
                "Medical literature source identity mismatch.");

        if (proposal.ArtifactId != artifactId)
            throw new InvalidOperationException(
                "Medical literature artifact identity mismatch.");

        var candidateIds =
            candidateRequirements.Select(x => x.Id).ToHashSet();

        if (candidateIds.Count != candidateRequirements.Count)
            throw new InvalidOperationException(
                "Candidate requirements contain duplicate identities.");

        var keys = new HashSet<(RequirementId, string)>();

        foreach (var classification in proposal.Classifications)
        {
            if (!candidateIds.Contains(classification.RequirementId))
                throw new InvalidOperationException(
                    $"Unknown candidate requirement " +
                    $"'{classification.RequirementId.Value}'.");

            if (classification.GuidanceRole is not (
                EvidenceGuidanceRoles.SupportsRequirement or
                EvidenceGuidanceRoles.EstablishesElement or
                EvidenceGuidanceRoles.Corroborates or
                EvidenceGuidanceRoles.Clarifies))
            {
                throw new InvalidOperationException(
                    $"Unsupported evidence guidance role " +
                    $"'{classification.GuidanceRole}'.");
            }

            if (string.IsNullOrWhiteSpace(classification.Description))
                throw new InvalidOperationException(
                    "A literature classification must contain a description.");

            if (!keys.Add(
                (classification.RequirementId,
                 classification.GuidanceRole)))
            {
                throw new InvalidOperationException(
                    "Duplicate literature requirement classification.");
            }

            if (classification.SourceExcerpts.Count == 0)
                throw new InvalidOperationException(
                    "A literature classification must contain " +
                    "at least one source excerpt.");

            foreach (var excerpt in classification.SourceExcerpts)
                ValidateExcerpt(artifactId, excerpt, sourceText);
        }
    }

    private static void ValidateExcerpt(
        ArtifactId artifactId,
        MedicalLiteratureSourceExcerpt excerpt,
        string sourceText)
    {
        ArgumentNullException.ThrowIfNull(excerpt);

        if (excerpt.ArtifactId != artifactId)
            throw new InvalidOperationException(
                "A literature source excerpt must reference " +
                "the classified artifact.");

        if (string.IsNullOrWhiteSpace(excerpt.Text))
            throw new InvalidOperationException(
                "A literature source excerpt cannot be empty.");

        if (excerpt.StartOffset is null || excerpt.Length is null)
            throw new InvalidOperationException(
                "A literature source excerpt must contain " +
                "a start offset and length.");

        var start = excerpt.StartOffset.Value;
        var length = excerpt.Length.Value;

        if (start < 0 || length < 0 ||
            length != excerpt.Text.Length ||
            length > sourceText.Length ||
            start > sourceText.Length - length)
        {
            throw new InvalidOperationException(
                "A literature source excerpt range is invalid.");
        }

        if (!sourceText.AsSpan(start, length)
                .SequenceEqual(excerpt.Text.AsSpan()))
        {
            throw new InvalidOperationException(
                "A literature source excerpt does not match " +
                "the source document.");
        }
    }
}
