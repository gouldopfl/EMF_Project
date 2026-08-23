using System.Text;
using EMF.Core.Models.Identities;
using EMF.Integrity;
using EMF.Orchestration.Services;
using EMF.Persistence.Storage;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class EmailAttachmentPipelineIntegrationTests
{
    [Fact]
    public async Task ProcessAsync_DecodesAndPersistsAttachment()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-email-{Guid.NewGuid():N}");

        try
        {
            var repository =
                new InMemoryEvidenceRepository();

            var contentStore =
                new FileSystemArtifactContentStore(root);

            var extraction =
                new EmailAttachmentExtractionService(
                    repository,
                    contentStore,
                    new Sha256ContentFingerprintService(),
                    new GuidArtifactIdGenerator(),
                    new ArtifactFactory());

            var service =
                new EmailAttachmentProcessingService(
                    new MimeKitEmailAttachmentDecoder(),
                    extraction);

            var emailArtifactId =
                new ArtifactId("email-parent-001");

            var eml =
                """
                From: sender@example.com
                To: recipient@example.com
                Subject: Evidence
                MIME-Version: 1.0
                Content-Type: multipart/mixed; boundary="abc"

                --abc
                Content-Type: text/plain

                Evidence attached.
                --abc
                Content-Type: text/plain; name="evidence.txt"
                Content-Disposition: attachment; filename="evidence.txt"
                Content-Transfer-Encoding: base64

                VmV0ZXJhbiBoYXMgY2hyb25pYyBpbnN0YWJpbGl0eS4=
                --abc--
                """;

            var results =
                await service.ProcessAsync(
                    emailArtifactId,
                    Encoding.UTF8.GetBytes(eml));

            var result = Assert.Single(results);

            Assert.Equal(
                "evidence.txt",
                result.Artifact.Name);

            var stored =
                await contentStore.ReadAsync(
                    result.Artifact.Id);

            Assert.NotNull(stored);
            Assert.Equal(
                "Veteran has chronic instability.",
                Encoding.UTF8.GetString(stored!));

            var relationships =
                await repository.GetRelationshipsAsync(
                    result.Artifact.Id);

            Assert.Contains(
                relationships,
                x =>
                    x.SourceArtifactId == emailArtifactId &&
                    x.TargetArtifactId == result.Artifact.Id &&
                    x.RelationshipType == "Contains");

            Assert.Contains(
                relationships,
                x =>
                    x.SourceArtifactId == result.Artifact.Id &&
                    x.TargetArtifactId == emailArtifactId &&
                    x.RelationshipType == "DerivedFrom");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
