using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;

namespace EMF.ConsoleApplication;

internal static class TextInsightConsoleEvidencePublisher
{
    public static async Task<Artifact> PublishAsync(
        string sourcePath,
        string contentHash,
        ArtifactId sourceArtifactId,
        string promotedBy,
        string reviewedBy,
        DateTimeOffset promotedUtc,
        IntelligenceAgentResult<TextInsight> result,
        string evidenceDatabasePath)
    {
        var repository =
            new SqliteEvidenceRepository(
                evidenceDatabasePath);

        await repository.InitializeAsync();

        var sourceArtifact =
            new Artifact
            {
                Id = sourceArtifactId,
                Name = Path.GetFileName(sourcePath),
                ArtifactType = "text",
                Fingerprint = new ContentFingerprint
                {
                    Algorithm = "SHA-256",
                    Value = contentHash
                },
                CreatedUtc = promotedUtc,
                Metadata =
                    new Dictionary<string, object>
                    {
                        ["sourcePath"] = sourcePath
                    }
            };

        await repository.AddArtifactWithProvenanceAsync(
            sourceArtifact,
            new Provenance
            {
                ArtifactId = sourceArtifactId,
                Source = sourcePath,
                RecordedUtc = promotedUtc,
                RecordedBy = promotedBy
            });

        var evidenceArtifact =
            new TextInsightEvidenceArtifactFactory()
                .Create(
                    result.Output!,
                    $"{Path.GetFileName(sourcePath)} insight",
                    promotedUtc);

        await new IntelligenceEvidencePromotionService(
                repository)
            .PromoteAsync(
                new IntelligenceEvidencePromotionRequest<
                    TextInsight>
                {
                    Artifact = evidenceArtifact,
                    IntelligenceResult = result,
                    PromotedBy = promotedBy,
                    PromotedUtc = promotedUtc,
                    ReviewedBy = reviewedBy,
                    ReviewedUtc = promotedUtc
                });

        return evidenceArtifact;
    }
}
