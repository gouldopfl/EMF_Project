using EMF.ConsoleApplication;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed partial class VeteransConsoleCommandTests
{
    [Fact]
    public async Task EvidenceSupersede_PersistsAndReusesRelationship()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-supersede-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            var unsignedId =
                new ArtifactId("statement-unsigned");
            var signedId =
                new ArtifactId("statement-signed");

            await repository.AddArtifactAsync(
                CreateArtifact(unsignedId));
            await repository.AddArtifactAsync(
                CreateArtifact(signedId));

            using var firstOutput = new StringWriter();

            var firstExitCode =
                await VeteransConsoleCommand.RunEvidenceSupersedeAsync(
                    databasePath,
                    signedId,
                    unsignedId,
                    firstOutput);

            using var secondOutput = new StringWriter();

            var secondExitCode =
                await VeteransConsoleCommand.RunEvidenceSupersedeAsync(
                    databasePath,
                    signedId,
                    unsignedId,
                    secondOutput);

            Assert.Equal(0, firstExitCode);
            Assert.Equal(0, secondExitCode);
            Assert.Contains(
                "Replacement Artifact : statement-signed",
                firstOutput.ToString());
            Assert.Contains(
                "Superseded Artifact  : statement-unsigned",
                firstOutput.ToString());
            Assert.Contains(
                "Status               : Persisted",
                firstOutput.ToString());
            Assert.Contains(
                "Status               : Existing",
                secondOutput.ToString());

            var relationship =
                Assert.Single(
                    (await repository.GetRelationshipsAsync(unsignedId))
                        .Where(
                            item =>
                                item.RelationshipType ==
                                    RelationshipTypes.Supersedes));

            Assert.Equal(signedId, relationship.SourceArtifactId);
            Assert.Equal(unsignedId, relationship.TargetArtifactId);
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task EvidenceSupersede_PublicRoutePersistsRelationship()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-supersede-route-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            var unsignedId =
                new ArtifactId("statement-unsigned");
            var signedId =
                new ArtifactId("statement-signed");

            await repository.AddArtifactAsync(
                CreateArtifact(unsignedId));
            await repository.AddArtifactAsync(
                CreateArtifact(signedId));

            var exitCode =
                await ConsoleCommandRouter.RunAsync(
                    [
                        "veterans",
                        "evidence",
                        "supersede",
                        databasePath,
                        signedId.Value,
                        unsignedId.Value
                    ]);

            Assert.Equal(0, exitCode);

            var relationship =
                Assert.Single(
                    (await repository.GetRelationshipsAsync(unsignedId))
                        .Where(
                            item =>
                                item.RelationshipType ==
                                    RelationshipTypes.Supersedes));

            Assert.Equal(signedId, relationship.SourceArtifactId);
            Assert.Equal(unsignedId, relationship.TargetArtifactId);
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static Artifact CreateArtifact(
        ArtifactId artifactId) =>
        new()
        {
            Id = artifactId,
            Name = $"{artifactId.Value}.pdf",
            ArtifactType = "file"
        };
}
